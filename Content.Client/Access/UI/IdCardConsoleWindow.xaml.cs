using System.Linq;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Roles;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using static Content.Shared.Access.Components.IdCardConsoleComponent;
using static Content.Shared.Shipyard.Components.ShuttleDeedComponent;

namespace Content.Client.Access.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class IdCardConsoleWindow : DefaultWindow
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly ILogManager _logManager = default!;
        private readonly ISawmill _logMill = default!;

        private readonly IdCardConsoleBoundUserInterface _owner;

        private AccessLevelControl _accessButtons = new();
        private readonly List<string> _jobPrototypeIds = new();

        private string? _lastFullName;
        private string? _lastJobTitle;
        private string?[]? _lastShuttleName;
        private string? _lastJobProto;
        private bool _interfaceEnabled = false;

        // The job that will be picked if the ID doesn't have a job on the station.
        private static ProtoId<JobPrototype> _defaultJob = "Contractor"; // Frontier: Passenger<Contractor

        public IdCardConsoleWindow(IdCardConsoleBoundUserInterface owner, IPrototypeManager prototypeManager,
            List<ProtoId<AccessLevelPrototype>> accessLevels)
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);
            _logMill = _logManager.GetSawmill(SharedIdCardConsoleSystem.Sawmill);

            _owner = owner;

            FullNameLineEdit.OnTextEntered += _ => SubmitData();
            FullNameLineEdit.OnTextChanged += _ =>
            {
                FullNameSaveButton.Disabled = FullNameSaveButton.Text == _lastFullName;
            };
            FullNameSaveButton.OnPressed += _ => SubmitData();

            JobTitleLineEdit.OnTextEntered += _ => SubmitData();
            JobTitleLineEdit.OnTextChanged += _ =>
            {
                JobTitleSaveButton.Disabled = JobTitleLineEdit.Text == _lastJobTitle;
            };
            JobTitleSaveButton.OnPressed += _ => SubmitData();

            ShipNameLineEdit.OnTextChanged += _ => EnsureValidShuttleName();
            ShipSuffixLineEdit.OnTextChanged += _ => EnsureValidShuttleName();
            ShipNameSaveButton.OnPressed += _ => SubmitShuttleData();

            var jobs = _prototypeManager.EnumeratePrototypes<JobPrototype>().ToList();
            jobs.Sort((x, y) => string.Compare(x.LocalizedName, y.LocalizedName, StringComparison.CurrentCulture));

            foreach (var job in jobs)
            {
                if (job.HideConsoleVisibility) // Frontier
                {
                    continue;
                }

                if (!job.OverrideConsoleVisibility.GetValueOrDefault(job.SetPreference))
                {
                    continue;
                }

                _jobPrototypeIds.Add(job.ID);
                JobPresetOptionButton.AddItem(Loc.GetString(job.Name), _jobPrototypeIds.Count - 1);
            }

            JobPresetOptionButton.OnItemSelected += SelectJobPreset;
            _accessButtons.Populate(accessLevels, prototypeManager);
            AccessLevelControlContainer.AddChild(_accessButtons);

            foreach (var (id, button) in _accessButtons.ButtonsList)
            {
                button.OnPressed += _ => SubmitData();
            }
        }

        private void ClearAllAccess()
        {
            foreach (var button in _accessButtons.ButtonsList.Values)
            {
                if (button.Pressed)
                {
                    button.Pressed = false;
                }
            }
        }

        private void SelectJobPreset(OptionButton.ItemSelectedEventArgs args)
        {
            if (!_prototypeManager.TryIndex(_jobPrototypeIds[args.Id], out JobPrototype? job))
            {
                return;
            }

            JobTitleLineEdit.Text = Loc.GetString(job.Name);
            args.Button.SelectId(args.Id);

            ClearAllAccess();

            // this is a sussy way to do this
            foreach (var access in job.Access)
            {
                if (_accessButtons.ButtonsList.TryGetValue(access, out var button) && !button.Disabled)
                {
                    button.Pressed = true;
                }
            }

            foreach (var group in job.AccessGroups)
            {
                if (!_prototypeManager.TryIndex(group, out AccessGroupPrototype? groupPrototype))
                {
                    continue;
                }

                foreach (var access in groupPrototype.Tags)
                {
                    if (_accessButtons.ButtonsList.TryGetValue(access, out var button) && !button.Disabled)
                    {
                        button.Pressed = true;
                    }
                }
            }

            SubmitData();
        }

        public void UpdateState(IdCardConsoleBoundUserInterfaceState state)
        {
            PrivilegedIdButton.Text = state.IsPrivilegedIdPresent
                ? Loc.GetString("id-card-console-window-eject-button")
                : Loc.GetString("id-card-console-window-insert-button");

            PrivilegedIdLabel.Text = state.PrivilegedIdName;

            TargetIdButton.Text = state.IsTargetIdPresent
                ? Loc.GetString("id-card-console-window-eject-button")
                : Loc.GetString("id-card-console-window-insert-button");

            TargetIdLabel.Text = state.TargetIdName;

            var interfaceEnabled =
                state.IsPrivilegedIdPresent && state.IsPrivilegedIdAuthorized && state.IsTargetIdPresent;

            var fullNameDirty = _lastFullName != null && FullNameLineEdit.Text != state.TargetIdFullName;
            var jobTitleDirty = _lastJobTitle != null && JobTitleLineEdit.Text != state.TargetIdJobTitle;

            FullNameLabel.Modulate = interfaceEnabled ? Color.White : Color.Gray;
            FullNameLineEdit.Editable = interfaceEnabled;
            if (!fullNameDirty)
            {
                FullNameLineEdit.Text = state.TargetIdFullName ?? string.Empty;
            }

            FullNameSaveButton.Disabled = !interfaceEnabled || !fullNameDirty;

            JobTitleLabel.Modulate = interfaceEnabled ? Color.White : Color.Gray;
            JobTitleLineEdit.Editable = interfaceEnabled;
            if (!jobTitleDirty)
            {
                JobTitleLineEdit.Text = state.TargetIdJobTitle ?? string.Empty;
            }

            JobTitleSaveButton.Disabled = !interfaceEnabled || !jobTitleDirty;

            // Frontier - shuttle renaming support
            ShipNameLabel.Modulate = interfaceEnabled ? Color.White : Color.Gray;

            ShipNameLineEdit.Editable = interfaceEnabled && state.HasOwnedShuttle;
            ShipSuffixLineEdit.Editable = false; // "Make sure you cannot change the suffix at all." - @dvir001, 2023.11.16

            if (interfaceEnabled && state.HasOwnedShuttle)
            {
                var parts = state.TargetShuttleNameParts ?? new string?[] { null, null };
                ShipNameLineEdit.Text = !interfaceEnabled ? string.Empty : parts[0] ?? string.Empty;
                ShipSuffixLineEdit.Text = !interfaceEnabled ? string.Empty : parts[1] ?? string.Empty;

                ShipNameSaveButton.Disabled = !interfaceEnabled || !state.HasOwnedShuttle;
            }
            else
            {
                ShipSuffixLineEdit.Text = string.Empty;
                ShipNameLineEdit.Text = !state.HasOwnedShuttle
                    ? Loc.GetString("id-card-console-window-shuttle-placeholder")
                    : string.Empty;
                ShipNameSaveButton.Disabled = true;
            }

            JobPresetOptionButton.Disabled = !interfaceEnabled;

            _accessButtons.UpdateState(state.TargetIdAccessList?.ToList() ??
                                       new List<ProtoId<AccessLevelPrototype>>(),
                                       state.AllowedModifyAccessList?.ToList() ??
                                       new List<ProtoId<AccessLevelPrototype>>());

            var jobIndex = _jobPrototypeIds.IndexOf(state.TargetIdJobPrototype);
            // If the job index is < 0 that means they don't have a job registered in the station records.
            // For example, a new ID from a box would have no job index.
            if (jobIndex < 0)
            {
                jobIndex = _jobPrototypeIds.IndexOf(_defaultJob);
            }

            JobPresetOptionButton.SelectId(jobIndex);

            _lastFullName = state.TargetIdFullName;
            _lastJobTitle = state.TargetIdJobTitle;
            _lastJobProto = state.TargetIdJobPrototype;
            _lastShuttleName = state.TargetShuttleNameParts;
            _interfaceEnabled = interfaceEnabled;

            EnsureValidShuttleName();
        }

        // <summary>
        // Invoked when a shuttle name field is edited.
        // Checks whether the name is valid and, if it is, enabled the save button.
        //
        // The form of a valid name is: "<CORP> <NAME> <SUFFIX>"
        // Where <CORP> is usually a 2-5 character string like NT14, KC, SL;
        // <NAME> is the shuttle name like Construct;
        // and <SUFFIX> is an immutable ID like QT-225.
        // </summary>
        private void EnsureValidShuttleName()
        {
            var name = ShipNameLineEdit.Text;
            var suffix = ShipSuffixLineEdit.Text;

            // We skip suffix validation because it's immutable and is ignored by the server
            var valid = name.Length <= MaxNameLength
                && name.Trim().Length >= 3; // Arbitrary client-side number, should hopefully be long enough.

            ShipNameSaveButton.Disabled = !_interfaceEnabled || !valid;

            // If still enabled, check for dirtiness and disable it if the name is not dirty
            if (!ShipNameSaveButton.Disabled)
            {
                var dirty = _lastShuttleName != null &&
                    ((_lastShuttleName[0] ?? string.Empty) != name
                    || (_lastShuttleName[1] ?? string.Empty) != suffix);

                ShipNameSaveButton.Disabled = !dirty;
            }
        }

        private void SubmitData()
        {
            // Don't send this if it isn't dirty.
            var jobProtoDirty = _lastJobProto != null &&
                                _jobPrototypeIds[JobPresetOptionButton.SelectedId] != _lastJobProto;

            _owner.SubmitData(
                FullNameLineEdit.Text,
                JobTitleLineEdit.Text,
                // Iterate over the buttons dictionary, filter by `Pressed`, only get key from the key/value pair
                _accessButtons.ButtonsList.Where(x => x.Value.Pressed).Select(x => x.Key).ToList(),
                jobProtoDirty ? _jobPrototypeIds[JobPresetOptionButton.SelectedId] : string.Empty);
        }

        private void SubmitShuttleData()
        {
            _owner.SubmitShipData(
                ShipNameLineEdit.Text,
                ShipSuffixLineEdit.Text);
        }
    }
}
