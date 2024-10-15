using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Timing;

namespace Content.Client.Info;

[GenerateTypedNameReferences]
public sealed partial class RulesPopup : Control
{
    private float _timer;

    public float Timer
    {
        get => _timer;
        set
        {
            WaitLabel.Text = Loc.GetString("ui-rules-wait", ("time", MathF.Floor(value)));
            _timer = value;
        }
    }

    public event Action? OnQuitPressed;
    public event Action? OnAcceptPressed;

    public RulesPopup()
    {
        RobustXamlLoader.Load(this);

        AcceptButton.OnPressed += OnAcceptButtonPressed;
        QuitButton.OnPressed += OnQuitButtonPressed;
    }

    private void OnQuitButtonPressed(BaseButton.ButtonEventArgs obj)
    {
        OnQuitPressed?.Invoke();
    }

    private void OnAcceptButtonPressed(BaseButton.ButtonEventArgs obj)
    {
        OnAcceptPressed?.Invoke();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!AcceptButton.Disabled)
            return;

        if (Timer > 0.0)
        {
            if (Timer - args.DeltaSeconds < 0)
                Timer = 0;
            else
                Timer -= args.DeltaSeconds;
        }
        else
        {
            AcceptButton.Disabled = false;
        }
    }
}
