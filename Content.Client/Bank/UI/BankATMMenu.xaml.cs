using Content.Client.UserInterface.Controls;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.Bank.UI;

[GenerateTypedNameReferences]
public sealed partial class BankATMMenu : FancyWindow
{
    public Action? WithdrawRequest;
    public Action? DepositRequest;
    public int Amount;
    public BankATMMenu()
    {
        RobustXamlLoader.Load(this);
        WithdrawButton.OnPressed += OnWithdrawPressed;
        DepositButton.OnPressed += OnDepositPressed;
        Title = Loc.GetString("bank-atm-menu-title");
        WithdrawEdit.OnTextChanged += OnAmountChanged;
    }

    public void SetBalance(int amount)
    {
        BalanceLabel.Text = Loc.GetString("cargo-console-menu-points-amount", ("amount", amount.ToString()));
    }

    public void SetDeposit(int amount)
    {
        DepositButton.Disabled = amount <= 0;
        DepositLabel.Text = Loc.GetString("cargo-console-menu-points-amount", ("amount", amount.ToString()));
    }

    public void SetEnabled(bool enabled)
    {
        WithdrawButton.Disabled = !enabled;
    }

    private void OnWithdrawPressed(BaseButton.ButtonEventArgs obj)
    {
        WithdrawRequest?.Invoke();
    }

    private void OnDepositPressed(BaseButton.ButtonEventArgs obj)
    {
        DepositRequest?.Invoke();
    }

    private void OnAmountChanged(LineEdit.LineEditEventArgs args)
    {
        if (int.TryParse(args.Text, out var amount))
        {
            Amount = amount;
        }    
    }
}
