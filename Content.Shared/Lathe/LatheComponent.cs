using Content.Shared.Construction.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Lathe
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
    public sealed partial class LatheComponent : Component
    {
        /// <summary>
        /// All of the recipes that the lathe has by default
        /// </summary>
        [DataField]
        public List<ProtoId<LatheRecipePrototype>> StaticRecipes = new();

        /// <summary>
        /// All of the recipes that the lathe is capable of researching
        /// </summary>
        [DataField]
        public List<ProtoId<LatheRecipePrototype>> DynamicRecipes = new();

        /// <summary>
        /// The lathe's construction queue
        /// </summary>
        [DataField]
        public List<LatheRecipePrototype> Queue = new();

        /// <summary>
        /// The sound that plays when the lathe is producing an item, if any
        /// </summary>
        [DataField]
        public SoundSpecifier? ProducingSound;

        #region Visualizer info
        [DataField]
        public string? IdleState;

        [DataField]
        public string? RunningState;
        #endregion

        /// <summary>
        /// The recipe the lathe is currently producing
        /// </summary>
        [ViewVariables]
        public LatheRecipePrototype? CurrentRecipe;

        #region MachineUpgrading
        /// <summary>
        /// A modifier that changes how long it takes to print a recipe
        /// </summary>
        [DataField, ViewVariables(VVAccess.ReadWrite)]
        public float TimeMultiplier = 1;

        /// <summary>
        /// A modifier that changes how much of a material is needed to print a recipe
        /// </summary>
        [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
        public float MaterialUseMultiplier = 1;

        /// <summary>
        /// A modifier that changes how long it takes to print a recipe
        /// </summary>
        [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
        public float FinalTimeMultiplier = 1;

        /// <summary>
        /// A modifier that changes how much of a material is needed to print a recipe
        /// </summary>
        [DataField, ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
        public float FinalMaterialUseMultiplier = 1;

        public const float DefaultPartRatingMaterialUseMultiplier = 0.85f; // Frontier: restored for machine parts

        //Frontier Upgrade Code Restore
        /// <summary>
        /// The machine part that reduces how long it takes to print a recipe.
        /// </summary>
        [DataField]
        public ProtoId<MachinePartPrototype> MachinePartPrintSpeed = "Manipulator";

        /// <summary>
        /// The value that is used to calculate the modified <see cref="TimeMultiplier"/>
        /// </summary>
        [DataField]
        public float PartRatingPrintTimeMultiplier = 0.5f;

        /// <summary>
        /// The machine part that reduces how much material it takes to print a recipe.
        /// </summary>
        [DataField]
        public ProtoId<MachinePartPrototype> MachinePartMaterialUse = "MatterBin";

        // Frontier: restored for machine part upgrades
        /// <summary>
        /// The value that is used to calculate the modifier <see cref="MaterialUseMultiplier"/>
        /// </summary>
        [DataField]
        public float PartRatingMaterialUseMultiplier = DefaultPartRatingMaterialUseMultiplier;
        // End Frontier
        #endregion
    }

    public sealed class LatheGetRecipesEvent : EntityEventArgs
    {
        public readonly EntityUid Lathe;

        public bool getUnavailable;

        public List<ProtoId<LatheRecipePrototype>> Recipes = new();

        public LatheGetRecipesEvent(EntityUid lathe, bool forced)
        {
            Lathe = lathe;
            getUnavailable = forced;
        }
    }

    /// <summary>
    /// Event raised on a lathe when it starts producing a recipe.
    /// </summary>
    [ByRefEvent]
    public readonly record struct LatheStartPrintingEvent(LatheRecipePrototype Recipe);
}
