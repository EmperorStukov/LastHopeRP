using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.DetailExaminable;
using Content.Server.Humanoid;
using Content.Server.IdentityManagement;
using Content.Server.Mind.Commands;
using Content.Server.PDA;
using Content.Server.Shuttles.Systems;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Clothing;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Station;
using Content.Shared.StatusIcon;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using Content.Server.Spawners.Components;
using Content.Shared.Bank.Components;
using Content.Shared._NF.Bank.Events;
using FastAccessors.Monads;
using Robust.Server.Player;
using Content.Shared.Corvax.Language.Components;
using Content.Shared.Hands.EntitySystems; // DeltaV

namespace Content.Server.Station.Systems;

/// <summary>
/// Manages spawning into the game, tracking available spawn points.
/// Also provides helpers for spawning in the player's mob.
/// </summary>
[PublicAPI]
public sealed class StationSpawningSystem : SharedStationSpawningSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoidSystem = default!;
    [Dependency] private readonly ActorSystem _actors = default!;
    [Dependency] private readonly IdCardSystem _cardSystem = default!;
    [Dependency] private readonly PdaSystem _pdaSystem = default!;
    [Dependency] private readonly SharedAccessSystem _accessSystem = default!;
    [Dependency] private readonly IdentitySystem _identity = default!;
    [Dependency] private readonly MetaDataSystem _metaSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    [Dependency] private readonly ArrivalsSystem _arrivalsSystem = default!;
    [Dependency] private readonly ContainerSpawnPointSystem _containerSpawnPointSystem = default!;
    [Dependency] private readonly IDependencyCollection _dependencyCollection = default!; // Frontier

    private bool _randomizeCharacters;

    private Dictionary<SpawnPriorityPreference, Action<PlayerSpawningEvent>> _spawnerCallbacks = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configurationManager, CCVars.ICRandomCharacters, e => _randomizeCharacters = e, true);

        _spawnerCallbacks = new Dictionary<SpawnPriorityPreference, Action<PlayerSpawningEvent>>()
        {
            { SpawnPriorityPreference.Arrivals, _arrivalsSystem.HandlePlayerSpawning },
            {
                SpawnPriorityPreference.Cryosleep, ev =>
                {
                    if (_arrivalsSystem.Forced)
                        _arrivalsSystem.HandlePlayerSpawning(ev);
                    else
                        _containerSpawnPointSystem.HandlePlayerSpawning(ev);
                }
            }
        };
    }

    /// <summary>
    /// Attempts to spawn a player character onto the given station.
    /// </summary>
    /// <param name="station">Station to spawn onto.</param>
    /// <param name="job">The job to assign, if any.</param>
    /// <param name="profile">The character profile to use, if any.</param>
    /// <param name="stationSpawning">Resolve pattern, the station spawning component for the station.</param>
    /// <param name="spawnPointType">Delta-V: Set desired spawn point type.</param>
    /// <returns>The resulting player character, if any.</returns>
    /// <exception cref="ArgumentException">Thrown when the given station is not a station.</exception>
    /// <remarks>
    /// This only spawns the character, and does none of the mind-related setup you'd need for it to be playable.
    /// </remarks>
    public EntityUid? SpawnPlayerCharacterOnStation(EntityUid? station, JobComponent? job, HumanoidCharacterProfile? profile, StationSpawningComponent? stationSpawning = null, SpawnPointType spawnPointType = SpawnPointType.Unset)
    {
        if (station != null && !Resolve(station.Value, ref stationSpawning))
            throw new ArgumentException("Tried to use a non-station entity as a station!", nameof(station));

        // Delta-V: Set desired spawn point type.
        var ev = new PlayerSpawningEvent(job, profile, station, spawnPointType);

        if (station != null && profile != null)
        {
            // Try to call the character's preferred spawner first.
            if (_spawnerCallbacks.TryGetValue(profile.SpawnPriority, out var preferredSpawner))
            {
                preferredSpawner(ev);

                foreach (var (key, remainingSpawner) in _spawnerCallbacks)
                {
                    if (key == profile.SpawnPriority)
                        continue;

                    remainingSpawner(ev);
                }
            }
            else
            {
                // Call all of them in the typical order.
                foreach (var typicalSpawner in _spawnerCallbacks.Values)
                {
                    typicalSpawner(ev);
                }
            }
        }

        RaiseLocalEvent(ev);

        DebugTools.Assert(ev.SpawnResult is { Valid: true } or null);

        return ev.SpawnResult;
    }

    //TODO: Figure out if everything in the player spawning region belongs somewhere else.
    #region Player spawning helpers

    /// <summary>
    /// Spawns in a player's mob according to their job and character information at the given coordinates.
    /// Used by systems that need to handle spawning players.
    /// </summary>
    /// <param name="coordinates">Coordinates to spawn the character at.</param>
    /// <param name="job">Job to assign to the character, if any.</param>
    /// <param name="profile">Appearance profile to use for the character.</param>
    /// <param name="station">The station this player is being spawned on.</param>
    /// <param name="entity">The entity to use, if one already exists.</param>
    /// <returns>The spawned entity</returns>
    public EntityUid SpawnPlayerMob(
        EntityCoordinates coordinates,
        JobComponent? job,
        HumanoidCharacterProfile? profile,
        EntityUid? station,
        EntityUid? entity = null)
    {
        _prototypeManager.TryIndex(job?.Prototype ?? string.Empty, out var prototype);

        // If we're not spawning a humanoid, we're gonna exit early without doing all the humanoid stuff.
        if (prototype?.JobEntity != null)
        {
            DebugTools.Assert(entity is null);
            var jobEntity = EntityManager.SpawnEntity(prototype.JobEntity, coordinates);
            MakeSentientCommand.MakeSentient(jobEntity, EntityManager);
            DoJobSpecials(job, jobEntity);
            _identity.QueueIdentityUpdate(jobEntity);
            return jobEntity;
        }

        string speciesId;
        if (_randomizeCharacters)
        {
            var weightId = _configurationManager.GetCVar(CCVars.ICRandomSpeciesWeights);
            var weights = _prototypeManager.Index<WeightedRandomSpeciesPrototype>(weightId);
            speciesId = weights.Pick(_random);
        }
        else if (profile != null)
        {
            speciesId = profile.Species;
        }
        else
        {
            speciesId = SharedHumanoidAppearanceSystem.DefaultSpecies;
        }

        if (!_prototypeManager.TryIndex<SpeciesPrototype>(speciesId, out var species))
            throw new ArgumentException($"Invalid species prototype was used: {speciesId}");

        entity ??= Spawn(species.Prototype, coordinates);

        if (_randomizeCharacters)
        {
            profile = HumanoidCharacterProfile.RandomWithSpecies(speciesId);
        }

        var jobLoadout = LoadoutSystem.GetJobPrototype(prototype?.ID);
        var bankBalance = profile!.BankBalance; //Frontier

        if (_prototypeManager.TryIndex(jobLoadout, out RoleLoadoutPrototype? roleProto))
        {
            RoleLoadout? loadout = null;

            profile?.Loadouts.TryGetValue(jobLoadout, out loadout);

            // Set to default if not present
            if (loadout == null)
            {
                loadout = new RoleLoadout(jobLoadout);
                loadout.SetDefault(profile, _actors.GetSession(entity), _prototypeManager);
            }

            // Order loadout selections by the order they appear on the prototype.
            foreach (var group in loadout.SelectedLoadouts.OrderBy(x => roleProto.Groups.FindIndex(e => e == x.Key)))
            {
                List<ProtoId<LoadoutPrototype>> equippedItems = new(); //Frontier - track purchased items (list: few items)
                foreach (var items in group.Value)
                {
                    if (!_prototypeManager.TryIndex(items.Prototype, out var loadoutProto))
                    {
                        Log.Error($"Unable to find loadout prototype for {items.Prototype}");
                        continue;
                    }

                    if (!_prototypeManager.TryIndex(loadoutProto.Equipment, out var startingGear))
                    {
                        Log.Error($"Unable to find starting gear {loadoutProto.Equipment} for loadout {loadoutProto}");
                        continue;
                    }

                    // Handle any extra data here.

                    //Frontier - we handle bank stuff so we are wrapping each item spawn inside our own cached check.
                    //This way, we will spawn every item we can afford in the order that they were originally sorted.
                    if (loadoutProto.Price <= bankBalance)
                    {
                        bankBalance -= loadoutProto.Price;
                        EquipStartingGear(entity.Value, startingGear, raiseEvent: false);
                        equippedItems.Add(loadoutProto.ID);
                    }
                }

                // New Frontiers - Loadout Fallbacks - if a character cannot afford their current job loadout, ensure they have fallback items for mandatory categories.
                // This code is licensed under AGPLv3. See AGPLv3.txt
                if (_prototypeManager.TryIndex(group.Key, out var groupPrototype) &&
                    equippedItems.Count < groupPrototype.MinLimit)
                {
                    foreach (var fallback in groupPrototype.Fallbacks)
                    {
                        // Do not duplicate items in loadout
                        if (equippedItems.Contains(fallback))
                        {
                            continue;
                        }

                        if (!_prototypeManager.TryIndex(fallback, out var loadoutProto))
                        {
                            Log.Error($"Unable to find loadout prototype for fallback {fallback}");
                            continue;
                        }

                        // Validate effects against the current character.
                        if (!loadout.IsValid(profile!, _actors.GetSession(entity!), fallback, _dependencyCollection, out var _))
                        {
                            continue;
                        }

                        if (!_prototypeManager.TryIndex(loadoutProto.Equipment, out var startingGear))
                        {
                            Log.Error($"Unable to find starting gear {loadoutProto.Equipment} for fallback loadout {loadoutProto}");
                            continue;
                        }

                        EquipStartingGear(entity.Value, startingGear, raiseEvent: false);
                        equippedItems.Add(fallback);
                        // Minimum number of items equipped, no need to load more prototypes.
                        if (equippedItems.Count >= groupPrototype.MinLimit)
                            break;
                    }
                }
                // End of modified code.
            }

            // Frontier: do not re-equip roleLoadout.
            // Frontier: DO equip job startingGear.
            if (prototype?.StartingGear is not null)
                EquipStartingGear(entity.Value, prototype.StartingGear, raiseEvent: false);

            if (HasComp<GiveTranslatorComponent>(entity.Value))
            {
                var coords = _transform.GetMapCoordinates(entity.Value);
                var translatorEntity = EntityManager.SpawnEntity("Translator", coords);
                _hands.TryForcePickupAnyHand(entity.Value, translatorEntity, checkActionBlocker: false);
            }

            var bank = EnsureComp<BankAccountComponent>(entity.Value);
            bank.Balance = bankBalance;

            if (_playerManager.TryGetSessionByEntity(entity.Value, out var player))
            {
                RaiseLocalEvent(new BalanceChangedEvent(bankBalance, player));
            }
        }

        var gearEquippedEv = new StartingGearEquippedEvent(entity.Value);
        RaiseLocalEvent(entity.Value, ref gearEquippedEv);

        if (profile != null)
        {
            if (prototype != null)
                SetPdaAndIdCardData(entity.Value, profile.Name, prototype, station);

            _humanoidSystem.LoadProfile(entity.Value, profile);
            _metaSystem.SetEntityName(entity.Value, profile.Name);
            if (profile.FlavorText != "" && _configurationManager.GetCVar(CCVars.FlavorText))
            {
                AddComp<DetailExaminableComponent>(entity.Value).Content = profile.FlavorText;
            }
        }

        DoJobSpecials(job, entity.Value);
        _identity.QueueIdentityUpdate(entity.Value);
        return entity.Value;
    }

    private void DoJobSpecials(JobComponent? job, EntityUid entity)
    {
        if (!_prototypeManager.TryIndex(job?.Prototype ?? string.Empty, out JobPrototype? prototype))
            return;

        foreach (var jobSpecial in prototype.Special)
        {
            jobSpecial.AfterEquip(entity);
        }
    }

    /// <summary>
    /// Sets the ID card and PDA name, job, and access data.
    /// </summary>
    /// <param name="entity">Entity to load out.</param>
    /// <param name="characterName">Character name to use for the ID.</param>
    /// <param name="jobPrototype">Job prototype to use for the PDA and ID.</param>
    /// <param name="station">The station this player is being spawned on.</param>
    public void SetPdaAndIdCardData(EntityUid entity, string characterName, JobPrototype jobPrototype, EntityUid? station)
    {
        if (!InventorySystem.TryGetSlotEntity(entity, "id", out var idUid))
            return;

        var cardId = idUid.Value;
        if (TryComp<PdaComponent>(idUid, out var pdaComponent) && pdaComponent.ContainedId != null)
            cardId = pdaComponent.ContainedId.Value;

        if (!TryComp<IdCardComponent>(cardId, out var card))
            return;

        _cardSystem.TryChangeFullName(cardId, characterName, card);
        _cardSystem.TryChangeJobTitle(cardId, jobPrototype.LocalizedName, card);

        if (_prototypeManager.TryIndex(jobPrototype.Icon, out var jobIcon))
            _cardSystem.TryChangeJobIcon(cardId, jobIcon, card);

        var extendedAccess = false;
        if (station != null)
        {
            var data = Comp<StationJobsComponent>(station.Value);
            extendedAccess = data.ExtendedAccess;
        }

        _accessSystem.SetAccessToJob(cardId, jobPrototype, extendedAccess);

        if (pdaComponent != null)
            _pdaSystem.SetOwner(idUid.Value, pdaComponent, characterName);
    }


    #endregion Player spawning helpers
}

/// <summary>
/// Ordered broadcast event fired on any spawner eligible to attempt to spawn a player.
/// This event's success is measured by if SpawnResult is not null.
/// You should not make this event's success rely on random chance.
/// This event is designed to use ordered handling. You probably want SpawnPointSystem to be the last handler.
/// </summary>
[PublicAPI]
public sealed class PlayerSpawningEvent : EntityEventArgs
{
    /// <summary>
    /// The entity spawned, if any. You should set this if you succeed at spawning the character, and leave it alone if it's not null.
    /// </summary>
    public EntityUid? SpawnResult;
    /// <summary>
    /// The job to use, if any.
    /// </summary>
    public readonly JobComponent? Job;
    /// <summary>
    /// The profile to use, if any.
    /// </summary>
    public readonly HumanoidCharacterProfile? HumanoidCharacterProfile;
    /// <summary>
    /// The target station, if any.
    /// </summary>
    public readonly EntityUid? Station;
    /// <summary>
    /// Delta-V: Desired SpawnPointType, if any.
    /// </summary>
    public readonly SpawnPointType DesiredSpawnPointType;

    public PlayerSpawningEvent(JobComponent? job, HumanoidCharacterProfile? humanoidCharacterProfile, EntityUid? station, SpawnPointType spawnPointType = SpawnPointType.Unset)
    {
        Job = job;
        HumanoidCharacterProfile = humanoidCharacterProfile;
        Station = station;
        DesiredSpawnPointType = spawnPointType;
    }
}
