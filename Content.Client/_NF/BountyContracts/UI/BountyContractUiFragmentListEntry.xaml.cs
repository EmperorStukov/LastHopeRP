using Content.Shared._NF.BountyContracts;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.XAML;

namespace Content.Client._NF.BountyContracts.UI;

[GenerateTypedNameReferences]
public sealed partial class BountyContractUiFragmentListEntry : Control
{
    public event Action<BountyContract>? OnRemoveButtonPressed;

    public BountyContractUiFragmentListEntry(BountyContract contract, bool canRemoveContracts)
    {
        RobustXamlLoader.Load(this);

        BountyName.Text = contract.Name;

        // vessel
        var vessel = !string.IsNullOrEmpty(contract.Vessel) ? contract.Vessel : Loc.GetString("bounty-contracts-ui-create-vessel-unknown");
        var vesselMsg = Loc.GetString("bounty-contracts-ui-list-vessel", ("vessel", vessel));
        BountyVessel.Text = vesselMsg;

        // bounty description
        var desc = !string.IsNullOrEmpty(contract.Description) ? contract.Description : Loc.GetString("bounty-contracts-ui-list-no-description");
        BountyDescription.SetMessage(desc);

        // author
        if (!string.IsNullOrEmpty(contract.Author))
        {
            var author = Loc.GetString("bounty-contracts-ui-list-author", ("author", contract.Author));
            BountyAuthor.Text = author;
        }

        // bounty reward
        BountyReward.Text = Loc.GetString("cargo-console-menu-points-amount",
            ("amount", contract.Reward.ToString()));

        // remove button
        RemoveButton.OnPressed += _ => OnRemoveButtonPressed?.Invoke(contract);
        RemoveButton.Disabled = !canRemoveContracts;

        // color
        var meta = SharedBountyContractSystem.CategoriesMeta[contract.Category];
        BountyPanel.ModulateSelfOverride = meta.UiColor;

        // category
        var category = Loc.GetString(meta.Name);
        BountyCategory.Text = Loc.GetString("bounty-contracts-ui-list-category",
            ("category", category));
    }
}
