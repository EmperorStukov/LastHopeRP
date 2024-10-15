using System.Linq;
using Content.Client.Administration.Systems;
using Content.Client.UserInterface.Controls;
using Content.Client.Verbs.UI;
using Content.Shared.Administration;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Input;

namespace Content.Client.Administration.UI.CustomControls
{
    [GenerateTypedNameReferences]
    public sealed partial class PlayerListControl : BoxContainer
    {
        private readonly AdminSystem _adminSystem;

        private List<PlayerInfo> _playerList = new();
        private readonly List<PlayerInfo> _sortedPlayerList = new();

        public event Action<PlayerInfo>? OnSelectionChanged;
        public IReadOnlyList<PlayerInfo> PlayerInfo => _playerList;

        public Func<PlayerInfo, string, string>? OverrideText;
        public Comparison<PlayerInfo>? Comparison;

        private IEntityManager _entManager;
        private IUserInterfaceManager _uiManager;

        private PlayerInfo? _selectedPlayer;

        public PlayerListControl()
        {
            _entManager = IoCManager.Resolve<IEntityManager>();
            _uiManager = IoCManager.Resolve<IUserInterfaceManager>();
            _adminSystem = _entManager.System<AdminSystem>();
            RobustXamlLoader.Load(this);
            // Fill the Option data
            PlayerListContainer.ItemPressed += PlayerListItemPressed;
            PlayerListContainer.ItemKeyBindDown += PlayerListItemKeyBindDown;
            PlayerListContainer.GenerateItem += GenerateButton;
            PopulateList(_adminSystem.PlayerList);
            FilterLineEdit.OnTextChanged += _ => FilterList();
            _adminSystem.PlayerListChanged += PopulateList;
            BackgroundPanel.PanelOverride = new StyleBoxFlat {BackgroundColor = new Color(32, 38, 32)};
        }

        private void PlayerListItemPressed(BaseButton.ButtonEventArgs? args, ListData? data)
        {
            if (args == null || data is not PlayerListData {Info: var selectedPlayer})
                return;

            if (selectedPlayer == _selectedPlayer)
                return;

            if (args.Event.Function != EngineKeyFunctions.UIClick)
                return;

            OnSelectionChanged?.Invoke(selectedPlayer);
            _selectedPlayer = selectedPlayer;

            // update label text. Only required if there is some override (e.g. unread bwoink count).
            if (OverrideText != null && args.Button.Children.FirstOrDefault()?.Children?.FirstOrDefault() is Label label)
                label.Text = GetText(selectedPlayer);
        }

        private void PlayerListItemKeyBindDown(GUIBoundKeyEventArgs? args, ListData? data)
        {
            if (args == null || data is not PlayerListData { Info: var selectedPlayer })
                return;

            if (args.Function != EngineKeyFunctions.UIRightClick || selectedPlayer.NetEntity == null)
                return;

            _uiManager.GetUIController<VerbMenuUIController>().OpenVerbMenu(selectedPlayer.NetEntity.Value, true);
            args.Handle();
        }

        public void StopFiltering()
        {
            FilterLineEdit.Text = string.Empty;
        }

        private void FilterList()
        {
            _sortedPlayerList.Clear();
            foreach (var info in _playerList)
            {
                var displayName = $"{info.CharacterName} ({info.Username})";
                if (info.IdentityName != info.CharacterName)
                    displayName += $" [{info.IdentityName}]";
                if (!string.IsNullOrEmpty(FilterLineEdit.Text)
                    && !displayName.ToLowerInvariant().Contains(FilterLineEdit.Text.Trim().ToLowerInvariant()))
                    continue;
                _sortedPlayerList.Add(info);
            }

            if (Comparison != null)
                _sortedPlayerList.Sort((a, b) => Comparison(a, b));

            PlayerListContainer.PopulateList(_sortedPlayerList.Select(info => new PlayerListData(info)).ToList());
            if (_selectedPlayer != null)
                PlayerListContainer.Select(new PlayerListData(_selectedPlayer));
        }

        public void PopulateList(IReadOnlyList<PlayerInfo>? players = null)
        {
            players ??= _adminSystem.PlayerList;

            _playerList = players.ToList();
            if (_selectedPlayer != null && !_playerList.Contains(_selectedPlayer))
                _selectedPlayer = null;

            FilterList();
        }

        private string GetText(PlayerInfo info)
        {
            var text = $"{info.CharacterName} ({info.Username})";
            if (OverrideText != null)
                text = OverrideText.Invoke(info, text);
            return text;
        }

        private void GenerateButton(ListData data, ListContainerButton button)
        {
            if (data is not PlayerListData { Info: var info })
                return;

            button.AddChild(new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Children =
                {
                    new Label
                    {
                        ClipText = true,
                        Text = GetText(info)
                    }
                }
            });

            button.AddStyleClass(ListContainer.StyleClassListContainerButton);
        }
    }

    public record PlayerListData(PlayerInfo Info) : ListData;
}
