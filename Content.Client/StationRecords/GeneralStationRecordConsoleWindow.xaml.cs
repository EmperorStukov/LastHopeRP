using System.Linq;
using Content.Client.Station;
using static Robust.Client.UserInterface.Controls.BaseButton;
using Content.Shared.StationRecords;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using Content.Shared.Roles;

namespace Content.Client.StationRecords;

[GenerateTypedNameReferences]
public sealed partial class GeneralStationRecordConsoleWindow : DefaultWindow
{
    public Action<uint?>? OnKeySelected;

    public Action<StationRecordFilterType, string>? OnFiltersChanged;
    public Action<uint>? OnDeleted;

    public event Action<ButtonEventArgs>? OnJobAdd;
    public event Action<ButtonEventArgs>? OnJobSubtract;

    private bool _isPopulating;

    private StationRecordFilterType _currentFilterType;

    public GeneralStationRecordConsoleWindow()
    {
        RobustXamlLoader.Load(this);

        _currentFilterType = StationRecordFilterType.Name;

        foreach (var item in Enum.GetValues<StationRecordFilterType>())
        {
            StationRecordsFilterType.AddItem(GetTypeFilterLocals(item), (int) item);
        }

        RecordListing.OnItemSelected += args =>
        {
            if (_isPopulating || RecordListing[args.ItemIndex].Metadata is not uint cast)
                return;

            OnKeySelected?.Invoke(cast);
        };

        RecordListing.OnItemDeselected += _ =>
        {
            if (!_isPopulating)
                OnKeySelected?.Invoke(null);
        };

        StationRecordsFilterType.OnItemSelected += eventArgs =>
        {
            var type = (StationRecordFilterType) eventArgs.Id;

            if (_currentFilterType != type)
            {
                _currentFilterType = type;
                FilterListingOfRecords();
            }
        };

        StationRecordsFiltersValue.OnTextEntered += args =>
        {
            FilterListingOfRecords(args.Text);
        };

        StationRecordsFilters.OnPressed += _ =>
        {
            FilterListingOfRecords(StationRecordsFiltersValue.Text);
        };

        StationRecordsFiltersReset.OnPressed += _ =>
        {
            StationRecordsFiltersValue.Text = "";
            FilterListingOfRecords();
        };
    }

    public void UpdateState(GeneralStationRecordConsoleState state)
    {
        if (state.Filter != null)
        {
            if (state.Filter.Type != _currentFilterType)
            {
                _currentFilterType = state.Filter.Type;
            }

            if (state.Filter.Value != StationRecordsFiltersValue.Text)
            {
                StationRecordsFiltersValue.Text = state.Filter.Value;
            }
        }

        StationRecordsFilterType.SelectId((int) _currentFilterType);

        if (state.JobList != null)
        {
            JobListing.Visible = true;
            PopulateJobsContainer(state.JobList);
        }

        if (state.RecordListing == null)
        {
            RecordListingStatus.Visible = true;
            RecordListing.Visible = true;
            RecordListingStatus.Text = Loc.GetString("general-station-record-console-empty-state");
            RecordContainer.Visible = true;
            RecordContainerStatus.Visible = true;
            return;
        }

        RecordListingStatus.Visible = false;
        RecordListing.Visible = true;
        RecordContainer.Visible = true;
        PopulateRecordListing(state.RecordListing!, state.SelectedKey);

        RecordContainerStatus.Visible = state.Record == null;

        if (state.Record != null)
        {
            RecordContainerStatus.Visible = state.SelectedKey == null;
            RecordContainerStatus.Text = state.SelectedKey == null
                ? Loc.GetString("general-station-record-console-no-record-found")
                : Loc.GetString("general-station-record-console-select-record-info");
            PopulateRecordContainer(state.Record, state.CanDeleteEntries, state.SelectedKey);
        }
        else
        {
            RecordContainer.RemoveAllChildren();
        }
    }
    private void PopulateRecordListing(Dictionary<uint, string> listing, uint? selected)
    {
        RecordListing.Clear();
        RecordListing.ClearSelected();

        _isPopulating = true;

        foreach (var (key, name) in listing)
        {
            var item = RecordListing.AddItem(name);
            item.Metadata = key;
            item.Selected = key == selected;
        }

        _isPopulating = false;

        RecordListing.SortItemsByText();
    }

    private void PopulateRecordContainer(GeneralStationRecord record, bool enableDelete, uint? id)
    {
        RecordContainer.RemoveAllChildren();
        var newRecord = new GeneralRecord(record, enableDelete, id);
        newRecord.OnDeletePressed = OnDeleted;

        RecordContainer.AddChild(newRecord);
    }

    private void FilterListingOfRecords(string text = "")
    {
        if (!_isPopulating)
        {
            OnFiltersChanged?.Invoke(_currentFilterType, text);
        }
    }

    private string GetTypeFilterLocals(StationRecordFilterType type)
    {
        return Loc.GetString($"general-station-record-{type.ToString().ToLower()}-filter");
    }

    // Frontier?
    private void PopulateJobsContainer(IReadOnlyDictionary<ProtoId<JobPrototype>, int?> jobList)
    {
        JobListing.RemoveAllChildren();
        foreach (var (job, amount) in jobList)
        {
            var jobEntry = new JobRow
            {
                Job = job,
                JobName = { Text = job },
                JobAmount = { Text = amount.ToString() },
            };
            jobEntry.DecreaseJobSlot.OnPressed += (args) => { OnJobSubtract?.Invoke(args); };
            jobEntry.IncreaseJobSlot.OnPressed += (args) => { OnJobAdd?.Invoke(args); };
            JobListing.AddChild(jobEntry);
        }
    }
    // End Frontier?
}