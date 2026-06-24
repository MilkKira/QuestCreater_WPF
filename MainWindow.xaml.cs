using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace QuestCreater_WPF;

public partial class MainWindow : Window
{
    private const string TarkynatorBaseUrl = "https://tarkynator.com/";
    private const string TarkynatorTasksUrl = "data/tarkov_data_tasks.json";
    private const string TarkynatorQuestsUrl = "data/quests.json";
    private const string DefaultSptQuestsPath =
        @"C:\Users\user\Documents\Github\server-csharp-main\server-csharp-main\Libraries\SPTarkov.Server.Assets\SPT_Data\database\templates\quests.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ObservableCollection<QuestRecord> _visibleQuests = [];
    private readonly ObservableCollection<RowText> _objectiveRows = [];
    private readonly ObservableCollection<ConditionRow> _conditionRows = [];
    private readonly ObservableCollection<RewardRow> _rewardRows = [];
    private readonly List<QuestRecord> _allQuests = [];
    private readonly Dictionary<string, TaskInfo> _taskInfoById = new(StringComparer.OrdinalIgnoreCase);
    private QuestRecord? _selectedQuest;
    private bool _isPopulatingForm;

    private readonly Dictionary<string, string> _traders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["54cb50c76803fa8b248b4571"] = "Prapor",
        ["54cb57776803fa99248b456e"] = "Therapist",
        ["579dc571d53a0658a154fbec"] = "Fence",
        ["58330581ace78e27b8b10cee"] = "Skier",
        ["5935c25fb3acc3127c3d8cd9"] = "Peacekeeper",
        ["5a7c2eca46aef81a7ca2145d"] = "Mechanic",
        ["5ac3b934156ae10c4430e83c"] = "Ragman",
        ["5c0647fdd443bc2504c2d371"] = "Jaeger",
        ["638f541a29ffd1183d187f57"] = "Lightkeeper",
        ["6617beeaa9cfa777ca915b7c"] = "Ref",
    };

    public MainWindow()
    {
        InitializeComponent();
        QuestList.ItemsSource = _visibleQuests;
        ObjectivesList.ItemsSource = _objectiveRows;
        ConditionsGrid.ItemsSource = _conditionRows;
        RewardsGrid.ItemsSource = _rewardRows;
        InitializePickLists();
        RefreshFilters();
        RefreshVisibleQuests();
    }

    private void InitializePickLists()
    {
        TraderIdBox.ItemsSource = _traders.Select(x => $"{x.Key} | {x.Value}").ToList();
        LocationBox.ItemsSource = new[]
        {
            "any", "factory4_day", "bigmap", "woods", "shoreline", "interchange", "laboratory",
            "rezervbase", "lighthouse", "tarkovstreets", "sandbox", "sandbox_high", "terminal",
        };
        QuestTypeBox.ItemsSource = new[]
        {
            "PickUp", "Elimination", "Discover", "Completion", "Exploration", "Levelling",
            "Experience", "Standing", "Loyalty", "Merchant", "Skill", "Multi", "WeaponAssembly",
            "ArenaWinMatch", "ArenaWinRound",
        };
        SideBox.ItemsSource = new[] { "Pmc", "Scav" };

        ConditionPhaseBox.ItemsSource = new[] { "AvailableForStart", "AvailableForFinish", "Started", "Success", "Fail" };
        ConditionPhaseBox.SelectedIndex = 0;
        ConditionPresetBox.ItemsSource = new[]
        {
            "Level",
            "Quest",
            "HandoverItem",
            "FindItem",
            "LeaveItemAtLocation",
            "PlaceBeacon",
            "VisitPlace",
            "VisitPlaceCounter",
            "SurviveLocation",
            "KillTarget",
            "Skill",
            "TraderLoyalty",
            "TraderStanding",
            "SellItemToTrader",
        };
        ConditionPresetBox.SelectedIndex = 0;

        RewardPhaseBox.ItemsSource = new[] { "Success", "Started", "Fail" };
        RewardPhaseBox.SelectedIndex = 0;
        RewardTypeBox.ItemsSource = new[] { "Experience", "TraderStanding", "Item", "TraderUnlock", "AssortmentUnlock" };
        RewardTypeBox.SelectedIndex = 0;
    }

    private async void LoadTarkynator_Click(object sender, RoutedEventArgs e)
    {
        await RunGuardedAsync("Loading Tarkynator quest data...", async () =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(TarkynatorBaseUrl), Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SPT-Quest-Creator/1.0");

            var tasksNode = JsonNode.Parse(await client.GetStringAsync(TarkynatorTasksUrl));
            LoadTaskInfo(tasksNode);

            var questsNode = JsonNode.Parse(await client.GetStringAsync(TarkynatorQuestsUrl));
            LoadQuestDatabase(questsNode, "Tarkynator");
            StatusText.Text = $"Loaded {_allQuests.Count} quests from Tarkynator.";
        });
    }

    private async void LoadLocalSpt_Click(object sender, RoutedEventArgs e)
    {
        await RunGuardedAsync("Loading local SPT quest templates...", async () =>
        {
            if (!File.Exists(DefaultSptQuestsPath))
            {
                throw new FileNotFoundException("Local SPT quests.json was not found.", DefaultSptQuestsPath);
            }

            var text = await File.ReadAllTextAsync(DefaultSptQuestsPath);
            var questsNode = JsonNode.Parse(text);
            LoadQuestDatabase(questsNode, "Local SPT");
            StatusText.Text = $"Loaded {_allQuests.Count} quests from local SPT.";
        });
    }

    private async void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import quests JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunGuardedAsync("Importing JSON...", async () =>
        {
            var text = await File.ReadAllTextAsync(dialog.FileName);
            var node = JsonNode.Parse(text);
            LoadQuestDatabase(node, Path.GetFileName(dialog.FileName));
            StatusText.Text = $"Imported {_allQuests.Count} quests.";
        });
    }

    private void NewQuest_Click(object sender, RoutedEventArgs e)
    {
        var id = MongoIdGenerator.NewId();
        var quest = CreateBlankQuest(id);
        var record = CreateRecord(id, quest, "Created");
        _allQuests.Insert(0, record);
        RefreshFilters();
        RefreshVisibleQuests();
        QuestList.SelectedItem = record;
        StatusText.Text = "Created a new quest template.";
    }

    private void CloneSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuest is null)
        {
            MessageBox.Show(this, "Select a quest to clone first.", "Clone quest", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ApplyFormToSelected();
        var newId = MongoIdGenerator.NewId();
        var clone = (JsonObject)_selectedQuest.Json.DeepClone();
        SetString(clone, "_id", newId);
        SetString(clone, "QuestName", $"{_selectedQuest.DisplayName} Copy");
        SetString(clone, "name", $"{newId} name");
        SetString(clone, "description", $"{newId} description");
        SetString(clone, "note", $"{newId} note");
        SetString(clone, "startedMessageText", $"{newId} startedMessageText");
        SetString(clone, "successMessageText", $"{newId} successMessageText");
        SetString(clone, "failMessageText", $"{newId} failMessageText");
        SetString(clone, "acceptPlayerMessage", $"{newId} acceptPlayerMessage");
        SetString(clone, "declinePlayerMessage", $"{newId} declinePlayerMessage");
        SetString(clone, "completePlayerMessage", $"{newId} completePlayerMessage");
        SetString(clone, "changeQuestMessageText", $"{newId} changeQuestMessageText");

        var record = CreateRecord(newId, clone, "Clone");
        _allQuests.Insert(0, record);
        RefreshFilters();
        RefreshVisibleQuests();
        QuestList.SelectedItem = record;
        StatusText.Text = "Cloned selected quest.";
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        ApplyFormToSelected();

        var dialog = new SaveFileDialog
        {
            Title = "Export complete Quest.json",
            FileName = "Quest.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunGuardedAsync("Exporting Quest.json...", async () =>
        {
            var root = new JsonObject();
            foreach (var quest in _allQuests.Where(q => !string.IsNullOrWhiteSpace(q.Id)).OrderBy(q => q.Id))
            {
                root[quest.Id] = quest.Json.DeepClone();
            }

            await File.WriteAllTextAsync(dialog.FileName, root.ToJsonString(JsonOptions));
            StatusText.Text = $"Exported {_allQuests.Count} quests to {dialog.FileName}.";
        });
    }

    private void Filter_Changed(object sender, EventArgs e)
    {
        if (_isPopulatingForm)
        {
            return;
        }

        RefreshVisibleQuests();
    }

    private void QuestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedQuest = QuestList.SelectedItem as QuestRecord;
        PopulateForm(_selectedQuest);
    }

    private void ApplyForm_Click(object sender, RoutedEventArgs e)
    {
        ApplyFormToSelected();
        RefreshVisibleQuests();
        StatusText.Text = "Applied form values to quest JSON.";
    }

    private void ApplyRawJson_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuest is null)
        {
            return;
        }

        try
        {
            var parsed = JsonNode.Parse(JsonPreviewBox.Text) as JsonObject
                ?? throw new InvalidDataException("Raw JSON must be an object.");
            _selectedQuest.Json = parsed;
            UpdateRecordFromJson(_selectedQuest);
            PopulateForm(_selectedQuest);
            RefreshFilters();
            RefreshVisibleQuests();
            StatusText.Text = "Applied raw JSON.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid JSON", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ConditionPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var preset = ConditionPresetBox.SelectedItem?.ToString() ?? "Level";
        ConditionTargetLabel.Content = preset switch
        {
            "Level" => "Target",
            "Quest" => "Quest ID",
            "HandoverItem" => "Item tpl",
            "FindItem" => "Item tpl",
            "LeaveItemAtLocation" => "Item tpl",
            "PlaceBeacon" => "Beacon tpl",
            "VisitPlace" => "Place ID",
            "VisitPlaceCounter" => "Place ID",
            "SurviveLocation" => "Location",
            "KillTarget" => "Target role",
            "Skill" => "Skill",
            "TraderLoyalty" => "Trader ID",
            "TraderStanding" => "Trader ID",
            "SellItemToTrader" => "Item tpl(s)",
            _ => "Target",
        };
        ConditionValueLabel.Content = preset switch
        {
            "Quest" => "Status",
            "FindItem" => "Count",
            "LeaveItemAtLocation" => "Count",
            "PlaceBeacon" => "Count",
            "VisitPlace" => "Value",
            "VisitPlaceCounter" => "Count",
            "SurviveLocation" => "Raid count",
            "KillTarget" => "Kill count",
            "Skill" => "Level",
            "TraderLoyalty" => "Level",
            "TraderStanding" => "Standing",
            "SellItemToTrader" => "Count / money",
            _ => "Value",
        };
        ConditionExtraLabel.Content = preset switch
        {
            "LeaveItemAtLocation" => "Zone ID | plantTime",
            "PlaceBeacon" => "Zone ID | plantTime",
            "SurviveLocation" => "Exit statuses",
            "SellItemToTrader" => "Trader ID",
            _ => "Extra",
        };
        ConditionTargetBox.Text = preset switch
        {
            "Level" => "player",
            "SurviveLocation" => "Woods",
            "KillTarget" => "Savage",
            "Skill" => "Sniper",
            _ => ConditionTargetBox.Text,
        };
        ConditionValueBox.Text = preset switch
        {
            "Level" => "1",
            "Quest" => "4",
            "HandoverItem" => "1",
            "FindItem" => "1",
            "LeaveItemAtLocation" => "1",
            "PlaceBeacon" => "1",
            "VisitPlace" => "1",
            "VisitPlaceCounter" => "1",
            "SurviveLocation" => "1",
            "KillTarget" => "1",
            "Skill" => "1",
            "TraderLoyalty" => "1",
            "TraderStanding" => "0",
            "SellItemToTrader" => "1",
            _ => ConditionValueBox.Text,
        };
        ConditionExtraBox.Text = preset switch
        {
            "LeaveItemAtLocation" => string.IsNullOrWhiteSpace(ConditionExtraBox.Text) ? "zone_id|30" : ConditionExtraBox.Text,
            "PlaceBeacon" => string.IsNullOrWhiteSpace(ConditionExtraBox.Text) ? "zone_id|30" : ConditionExtraBox.Text,
            "SurviveLocation" => "Survived,Runner,Transit",
            "SellItemToTrader" => string.IsNullOrWhiteSpace(ConditionExtraBox.Text) ? "54cb50c76803fa8b248b4571" : ConditionExtraBox.Text,
            _ => ConditionExtraBox.Text,
        };
    }

    private void RewardTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var type = RewardTypeBox.SelectedItem?.ToString() ?? "Experience";
        RewardTargetLabel.Content = type switch
        {
            "Experience" => "Target",
            "TraderStanding" => "Trader ID",
            "Item" => "Item tpl",
            "TraderUnlock" => "Trader ID",
            "AssortmentUnlock" => "Assort ID",
            _ => "Target",
        };
        RewardValueLabel.Content = type switch
        {
            "TraderStanding" => "Standing",
            "Item" => "Count",
            _ => "Value",
        };
        RewardTargetBox.Text = type == "Experience" ? "" : RewardTargetBox.Text;
        RewardValueBox.Text = type switch
        {
            "Experience" => "1000",
            "TraderStanding" => "0.01",
            "Item" => "1",
            _ => RewardValueBox.Text,
        };
    }

    private void AddCondition_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuest is null)
        {
            return;
        }

        ApplyFormToSelected();
        var phase = ConditionPhaseBox.SelectedItem?.ToString() ?? "AvailableForStart";
        var preset = ConditionPresetBox.SelectedItem?.ToString() ?? "Level";
        var target = ConditionTargetBox.Text.Trim();
        var valueText = ConditionValueBox.Text.Trim();
        var extraText = ConditionExtraBox.Text.Trim();
        var condition = CreateCondition(preset, target, valueText, extraText);
        GetConditionArray(_selectedQuest.Json, phase).Add(condition);
        UpdateRecordFromJson(_selectedQuest);
        PopulateConditionRows(_selectedQuest);
        UpdateJsonPreview();
        StatusText.Text = $"Added {preset} condition.";
    }

    private void AddReward_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedQuest is null)
        {
            return;
        }

        ApplyFormToSelected();
        var phase = RewardPhaseBox.SelectedItem?.ToString() ?? "Success";
        var type = RewardTypeBox.SelectedItem?.ToString() ?? "Experience";
        var target = RewardTargetBox.Text.Trim();
        var valueText = RewardValueBox.Text.Trim();
        var reward = CreateReward(type, target, valueText);
        GetRewardArray(_selectedQuest.Json, phase).Add(reward);
        UpdateRecordFromJson(_selectedQuest);
        PopulateRewardRows(_selectedQuest);
        UpdateJsonPreview();
        StatusText.Text = $"Added {type} reward.";
    }

    private async Task RunGuardedAsync(string status, Func<Task> action)
    {
        try
        {
            StatusText.Text = status;
            IsEnabled = false;
            await action();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error";
            MessageBox.Show(this, ex.Message, "Quest Creator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void LoadTaskInfo(JsonNode? tasksNode)
    {
        _taskInfoById.Clear();
        var tasks = tasksNode?["tasks"] as JsonObject;
        if (tasks is null)
        {
            return;
        }

        foreach (var item in tasks)
        {
            if (item.Value is not JsonObject task)
            {
                continue;
            }

            var id = GetString(task, "id") ?? item.Key;
            var objectives = new List<string>();
            if (task["objectives"] is JsonArray objectiveArray)
            {
                objectives.AddRange(
                    objectiveArray.OfType<JsonObject>()
                        .Select(x => GetString(x, "description"))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!));
            }

            _taskInfoById[id] = new TaskInfo(
                id,
                GetString(task, "name") ?? id,
                GetString(task["trader"] as JsonObject, "name") ?? string.Empty,
                GetString(task["map"] as JsonObject, "name") ?? "Any",
                GetString(task, "type") ?? string.Empty,
                GetInt(task, "minPlayerLevel"),
                objectives);
        }
    }

    private void LoadQuestDatabase(JsonNode? rootNode, string source)
    {
        if (rootNode is not JsonObject root)
        {
            throw new InvalidDataException("Quest data must be a JSON object keyed by quest id.");
        }

        _allQuests.Clear();
        foreach (var entry in root)
        {
            if (entry.Value is not JsonObject quest)
            {
                continue;
            }

            var id = GetString(quest, "_id") ?? entry.Key;
            _allQuests.Add(CreateRecord(id, (JsonObject)quest.DeepClone(), source));
        }

        RefreshFilters();
        RefreshVisibleQuests();
        QuestList.SelectedIndex = _visibleQuests.Count > 0 ? 0 : -1;
    }

    private QuestRecord CreateRecord(string id, JsonObject quest, string source)
    {
        var record = new QuestRecord { Id = id, Json = quest, Source = source };
        UpdateRecordFromJson(record);
        return record;
    }

    private void UpdateRecordFromJson(QuestRecord record)
    {
        var quest = record.Json;
        record.Id = GetString(quest, "_id") ?? record.Id;
        var task = _taskInfoById.GetValueOrDefault(record.Id);
        record.DisplayName = task?.Name ?? GetString(quest, "QuestName") ?? GetString(quest, "name") ?? record.Id;
        record.QuestName = GetString(quest, "QuestName") ?? record.DisplayName;
        record.TraderId = ExtractTraderId(GetString(quest, "traderId") ?? string.Empty);
        record.TraderName = task?.TraderName ?? TraderName(record.TraderId);
        record.MapName = task?.MapName ?? NormalizeMapName(GetString(quest, "location"));
        record.Type = task?.Type ?? GetString(quest, "type") ?? string.Empty;
        record.MinLevel = task?.MinPlayerLevel ?? FindLevelRequirement(quest);
        record.Objectives = task?.Objectives ?? [];
    }

    private void PopulateForm(QuestRecord? quest)
    {
        _isPopulatingForm = true;
        try
        {
            _conditionRows.Clear();
            _rewardRows.Clear();
            _objectiveRows.Clear();

            if (quest is null)
            {
                SelectedTitle.Text = "No quest selected";
                SelectedSubtitle.Text = "Load or create a quest to begin.";
                JsonPreviewBox.Text = string.Empty;
                return;
            }

            var json = quest.Json;
            SelectedTitle.Text = quest.DisplayName;
            SelectedSubtitle.Text = $"{quest.Id} | {quest.TraderName} | {quest.MapName} | {quest.Type}";
            QuestIdBox.Text = GetString(json, "_id") ?? quest.Id;
            QuestNameBox.Text = GetString(json, "QuestName") ?? string.Empty;
            NameKeyBox.Text = GetString(json, "name") ?? string.Empty;
            DescriptionKeyBox.Text = GetString(json, "description") ?? string.Empty;
            TraderIdBox.Text = GetString(json, "traderId") ?? string.Empty;
            LocationBox.Text = GetString(json, "location") ?? "any";
            QuestTypeBox.Text = GetString(json, "type") ?? "Completion";
            SideBox.Text = GetString(json, "side") ?? "Pmc";
            ImageBox.Text = GetString(json, "image") ?? "/files/quest/icon/default.jpg";
            NotificationsCheck.IsChecked = GetBool(json, "canShowNotificationsInGame") ?? true;
            RestartableCheck.IsChecked = GetBool(json, "restartable") ?? false;
            InstantCompleteCheck.IsChecked = GetBool(json, "instantComplete") ?? false;
            SecretQuestCheck.IsChecked = GetBool(json, "secretQuest") ?? false;

            foreach (var objective in quest.Objectives)
            {
                _objectiveRows.Add(new RowText(objective));
            }

            PopulateConditionRows(quest);
            PopulateRewardRows(quest);
            UpdateJsonPreview();
        }
        finally
        {
            _isPopulatingForm = false;
        }
    }

    private void PopulateConditionRows(QuestRecord quest)
    {
        _conditionRows.Clear();
        var conditions = quest.Json["conditions"] as JsonObject;
        if (conditions is null)
        {
            return;
        }

        foreach (var phase in new[] { "AvailableForStart", "AvailableForFinish", "Started", "Success", "Fail" })
        {
            if (conditions[phase] is not JsonArray array)
            {
                continue;
            }

            foreach (var node in array.OfType<JsonObject>())
            {
                _conditionRows.Add(new ConditionRow(
                    phase,
                    GetString(node, "conditionType") ?? string.Empty,
                    SummarizeCondition(node),
                    GetString(node, "id") ?? string.Empty));
            }
        }
    }

    private void PopulateRewardRows(QuestRecord quest)
    {
        _rewardRows.Clear();
        var rewards = quest.Json["rewards"] as JsonObject;
        if (rewards is null)
        {
            return;
        }

        foreach (var phase in new[] { "Started", "Success", "Fail" })
        {
            if (rewards[phase] is not JsonArray array)
            {
                continue;
            }

            foreach (var node in array.OfType<JsonObject>())
            {
                _rewardRows.Add(new RewardRow(
                    phase,
                    GetString(node, "type") ?? string.Empty,
                    SummarizeReward(node),
                    GetString(node, "id") ?? string.Empty));
            }
        }
    }

    private void ApplyFormToSelected()
    {
        if (_selectedQuest is null || _isPopulatingForm)
        {
            return;
        }

        var json = _selectedQuest.Json;
        var id = QuestIdBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(id))
        {
            SetString(json, "_id", id);
        }

        SetString(json, "QuestName", QuestNameBox.Text.Trim());
        SetString(json, "name", NameKeyBox.Text.Trim());
        SetString(json, "description", DescriptionKeyBox.Text.Trim());
        SetString(json, "traderId", ExtractTraderId(TraderIdBox.Text.Trim()));
        SetString(json, "location", LocationBox.Text.Trim());
        SetString(json, "type", QuestTypeBox.Text.Trim());
        SetString(json, "side", SideBox.Text.Trim());
        SetString(json, "image", ImageBox.Text.Trim());
        SetBool(json, "canShowNotificationsInGame", NotificationsCheck.IsChecked == true);
        SetBool(json, "restartable", RestartableCheck.IsChecked == true);
        SetBool(json, "instantComplete", InstantCompleteCheck.IsChecked == true);
        SetBool(json, "secretQuest", SecretQuestCheck.IsChecked == true);
        EnsureQuestShape(json);

        UpdateRecordFromJson(_selectedQuest);
        SelectedTitle.Text = _selectedQuest.DisplayName;
        SelectedSubtitle.Text = $"{_selectedQuest.Id} | {_selectedQuest.TraderName} | {_selectedQuest.MapName} | {_selectedQuest.Type}";
        UpdateJsonPreview();
    }

    private void RefreshFilters()
    {
        _isPopulatingForm = true;
        try
        {
            SetFilterItems(TraderFilter, _allQuests.Select(x => x.TraderName));
            SetFilterItems(MapFilter, _allQuests.Select(x => x.MapName));
            SetFilterItems(TypeFilter, _allQuests.Select(x => x.Type));
            SetFilterItems(SourceFilter, _allQuests.Select(x => x.Source));
        }
        finally
        {
            _isPopulatingForm = false;
        }
    }

    private static void SetFilterItems(ComboBox comboBox, IEnumerable<string> values)
    {
        var previous = comboBox.SelectedItem?.ToString() ?? "All";
        var items = new[] { "All" }.Concat(values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Order()).ToList();
        comboBox.ItemsSource = items;
        comboBox.SelectedItem = items.Contains(previous) ? previous : "All";
    }

    private void RefreshVisibleQuests()
    {
        var selectedId = _selectedQuest?.Id;
        var query = SearchBox.Text.Trim();
        var trader = TraderFilter.SelectedItem?.ToString() ?? "All";
        var map = MapFilter.SelectedItem?.ToString() ?? "All";
        var type = TypeFilter.SelectedItem?.ToString() ?? "All";
        var source = SourceFilter.SelectedItem?.ToString() ?? "All";

        var filtered = _allQuests.Where(q =>
            MatchesFilter(q.TraderName, trader)
            && MatchesFilter(q.MapName, map)
            && MatchesFilter(q.Type, type)
            && MatchesFilter(q.Source, source)
            && (string.IsNullOrWhiteSpace(query)
                || q.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || q.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || q.Objectives.Any(o => o.Contains(query, StringComparison.OrdinalIgnoreCase))));

        _visibleQuests.Clear();
        foreach (var quest in filtered.OrderBy(q => q.TraderName).ThenBy(q => q.DisplayName))
        {
            _visibleQuests.Add(quest);
        }

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            QuestList.SelectedItem = _visibleQuests.FirstOrDefault(q => q.Id == selectedId);
        }
    }

    private static bool MatchesFilter(string value, string filter)
    {
        return filter == "All" || string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateJsonPreview()
    {
        if (_selectedQuest is null)
        {
            JsonPreviewBox.Text = string.Empty;
            return;
        }

        JsonPreviewBox.Text = _selectedQuest.Json.ToJsonString(JsonOptions);
    }

    private JsonObject CreateBlankQuest(string id)
    {
        var quest = new JsonObject
        {
            ["QuestName"] = "New Quest",
            ["_id"] = id,
            ["acceptPlayerMessage"] = $"{id} acceptPlayerMessage",
            ["acceptanceAndFinishingSource"] = "eft",
            ["arenaLocations"] = new JsonArray(),
            ["canShowNotificationsInGame"] = true,
            ["changeQuestMessageText"] = $"{id} changeQuestMessageText",
            ["completePlayerMessage"] = $"{id} completePlayerMessage",
            ["conditions"] = new JsonObject
            {
                ["AvailableForStart"] = new JsonArray(CreateCondition("Level", "player", "1")),
                ["AvailableForFinish"] = new JsonArray(),
                ["Started"] = new JsonArray(),
                ["Success"] = new JsonArray(),
                ["Fail"] = new JsonArray(),
            },
            ["declinePlayerMessage"] = $"{id} declinePlayerMessage",
            ["description"] = $"{id} description",
            ["failMessageText"] = $"{id} failMessageText",
            ["gameModes"] = new JsonArray(),
            ["image"] = "/files/quest/icon/default.jpg",
            ["instantComplete"] = false,
            ["isKey"] = false,
            ["location"] = "any",
            ["name"] = $"{id} name",
            ["note"] = $"{id} note",
            ["progressSource"] = "eft",
            ["rankingModes"] = new JsonArray(),
            ["restartable"] = false,
            ["rewards"] = new JsonObject
            {
                ["Started"] = new JsonArray(),
                ["Success"] = new JsonArray(),
                ["Fail"] = new JsonArray(),
            },
            ["secretQuest"] = false,
            ["side"] = "Pmc",
            ["startedMessageText"] = $"{id} startedMessageText",
            ["successMessageText"] = $"{id} successMessageText",
            ["traderId"] = "54cb50c76803fa8b248b4571",
            ["type"] = "Completion",
        };

        EnsureQuestShape(quest);
        return quest;
    }

    private JsonObject CreateCondition(string preset, string target, string valueText, string extraText = "")
    {
        var id = MongoIdGenerator.NewId();
        var value = ParseDouble(valueText, preset == "Quest" ? 4 : 1);

        return preset switch
        {
            "Quest" => new JsonObject
            {
                ["availableAfter"] = 0,
                ["conditionType"] = "Quest",
                ["dispersion"] = 0,
                ["dynamicLocale"] = false,
                ["globalQuestCounterId"] = string.Empty,
                ["id"] = id,
                ["index"] = 0,
                ["parentId"] = string.Empty,
                ["status"] = new JsonArray((int)value),
                ["target"] = target,
                ["visibilityConditions"] = new JsonArray(),
            },
            "HandoverItem" => CreateItemCondition("HandoverItem", id, target, value, false),
            "FindItem" => CreateItemCondition("FindItem", id, target, value, countInRaid: false),
            "LeaveItemAtLocation" => CreatePlacementCondition("LeaveItemAtLocation", id, target, value, extraText),
            "PlaceBeacon" => CreatePlacementCondition("PlaceBeacon", id, target, value, extraText, includeDurability: false),
            "VisitPlace" => CreateDirectVisitPlaceCondition(id, target, value),
            "VisitPlaceCounter" => CreateCounterCreator(
                id,
                "Exploration",
                value,
                new JsonObject
                {
                    ["conditionType"] = "VisitPlace",
                    ["dynamicLocale"] = false,
                    ["id"] = MongoIdGenerator.NewId(),
                    ["target"] = string.IsNullOrWhiteSpace(target) ? "place_id" : target,
                    ["value"] = 1,
                }),
            "SurviveLocation" => CreateCounterCreator(
                id,
                "Exploration",
                value,
                new JsonObject
                {
                    ["conditionType"] = "Location",
                    ["dynamicLocale"] = false,
                    ["id"] = MongoIdGenerator.NewId(),
                    ["target"] = ToJsonArray(SplitList(target, "Woods")),
                },
                new JsonObject
                {
                    ["conditionType"] = "ExitStatus",
                    ["dynamicLocale"] = false,
                    ["id"] = MongoIdGenerator.NewId(),
                    ["status"] = ToJsonArray(SplitList(extraText, "Survived", "Runner", "Transit")),
                }),
            "KillTarget" => CreateCounterCreator(
                id,
                "Elimination",
                value,
                new JsonObject
                {
                    ["bodyPart"] = new JsonArray(),
                    ["compareMethod"] = ">=",
                    ["conditionType"] = "Kills",
                    ["daytime"] = new JsonObject { ["from"] = 0, ["to"] = 0 },
                    ["distance"] = new JsonObject { ["compareMethod"] = ">=", ["value"] = 0 },
                    ["dynamicLocale"] = false,
                    ["enemyEquipmentExclusive"] = new JsonArray(),
                    ["enemyEquipmentInclusive"] = new JsonArray(),
                    ["enemyHealthEffects"] = new JsonArray(),
                    ["id"] = MongoIdGenerator.NewId(),
                    ["resetOnSessionEnd"] = false,
                    ["savageRole"] = new JsonArray(),
                    ["target"] = string.IsNullOrWhiteSpace(target) ? "Savage" : target,
                    ["value"] = 1,
                    ["weapon"] = new JsonArray(),
                    ["weaponCaliber"] = new JsonArray(),
                    ["weaponModsExclusive"] = new JsonArray(),
                    ["weaponModsInclusive"] = new JsonArray(),
                }),
            "Skill" => CreateCompareCondition("Skill", id, target, value),
            "TraderLoyalty" => CreateCompareCondition("TraderLoyalty", id, ExtractTraderId(target), value),
            "TraderStanding" => CreateCompareCondition("TraderStanding", id, ExtractTraderId(target), value),
            "SellItemToTrader" => CreateSellItemToTraderCondition(id, target, value, extraText),
            _ => CreateCompareCondition("Level", id, string.Empty, value, includeTarget: false),
        };
    }

    private static JsonObject CreateCompareCondition(
        string conditionType,
        string id,
        string target,
        double value,
        string compareMethod = ">=",
        bool includeTarget = true)
    {
        var condition = new JsonObject
        {
            ["compareMethod"] = compareMethod,
            ["conditionType"] = conditionType,
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = string.Empty,
            ["id"] = id,
            ["index"] = 0,
            ["parentId"] = string.Empty,
            ["value"] = value,
            ["visibilityConditions"] = new JsonArray(),
        };

        if (includeTarget)
        {
            condition["target"] = target;
        }

        return condition;
    }

    private static JsonObject CreateItemCondition(string conditionType, string id, string target, double value, bool countInRaid)
    {
        var condition = new JsonObject
        {
            ["conditionType"] = conditionType,
            ["dogtagLevel"] = 0,
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = string.Empty,
            ["id"] = id,
            ["index"] = 0,
            ["isEncoded"] = false,
            ["maxDurability"] = 100,
            ["minDurability"] = 0,
            ["onlyFoundInRaid"] = false,
            ["parentId"] = string.Empty,
            ["target"] = ToJsonArray(SplitList(target, "item_tpl")),
            ["value"] = value,
            ["visibilityConditions"] = new JsonArray(),
        };

        if (conditionType == "FindItem")
        {
            condition["countInRaid"] = countInRaid;
        }

        return condition;
    }

    private static JsonObject CreatePlacementCondition(
        string conditionType,
        string id,
        string target,
        double value,
        string extraText,
        bool includeDurability = true)
    {
        var zone = ParseZoneAndPlantTime(extraText);
        var condition = new JsonObject
        {
            ["conditionType"] = conditionType,
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = string.Empty,
            ["id"] = id,
            ["index"] = 0,
            ["parentId"] = string.Empty,
            ["plantTime"] = zone.PlantTime,
            ["target"] = ToJsonArray(SplitList(target, "item_tpl")),
            ["value"] = value,
            ["visibilityConditions"] = new JsonArray(),
            ["zoneId"] = zone.ZoneId,
        };

        if (includeDurability)
        {
            condition["dogtagLevel"] = 0;
            condition["isEncoded"] = false;
            condition["maxDurability"] = 100;
            condition["minDurability"] = 0;
            condition["onlyFoundInRaid"] = false;
        }

        return condition;
    }

    private static JsonObject CreateDirectVisitPlaceCondition(string id, string target, double value)
    {
        return new JsonObject
        {
            ["conditionType"] = "VisitPlace",
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = string.Empty,
            ["id"] = id,
            ["index"] = 0,
            ["parentId"] = string.Empty,
            ["target"] = string.IsNullOrWhiteSpace(target) ? "place_id" : target,
            ["value"] = value,
            ["visibilityConditions"] = new JsonArray(),
        };
    }

    private static JsonObject CreateCounterCreator(string id, string questType, double value, params JsonObject[] counterConditions)
    {
        var conditions = new JsonArray();
        foreach (var condition in counterConditions)
        {
            conditions.Add(condition);
        }

        return new JsonObject
        {
            ["completeInSeconds"] = 0,
            ["conditionType"] = "CounterCreator",
            ["counter"] = new JsonObject
            {
                ["conditions"] = conditions,
                ["id"] = MongoIdGenerator.NewId(),
            },
            ["doNotResetIfCounterCompleted"] = false,
            ["dynamicLocale"] = false,
            ["globalQuestCounterId"] = string.Empty,
            ["id"] = id,
            ["index"] = 0,
            ["isNecessary"] = false,
            ["isResetOnConditionFailed"] = false,
            ["oneSessionOnly"] = false,
            ["parentId"] = string.Empty,
            ["type"] = questType,
            ["value"] = value,
            ["visibilityConditions"] = new JsonArray(),
        };
    }

    private static JsonObject CreateSellItemToTraderCondition(string id, string target, double value, string traderId)
    {
        var condition = CreateItemCondition("SellItemToTrader", id, target, value, countInRaid: false);
        condition["traderId"] = string.IsNullOrWhiteSpace(traderId) ? "54cb50c76803fa8b248b4571" : ExtractTraderId(traderId);
        return condition;
    }

    private JsonObject CreateReward(string type, string target, string valueText)
    {
        var id = MongoIdGenerator.NewId();
        var value = ParseDouble(valueText, type == "TraderStanding" ? 0.01 : 1);
        var reward = new JsonObject
        {
            ["availableInGameEditions"] = new JsonArray(),
            ["gameMode"] = new JsonArray("regular", "pve"),
            ["id"] = id,
            ["isHidden"] = false,
            ["type"] = type,
            ["unknown"] = false,
            ["value"] = value,
        };

        switch (type)
        {
            case "Item":
                var itemId = MongoIdGenerator.NewId();
                reward["findInRaid"] = false;
                reward["isEncoded"] = false;
                reward["target"] = itemId;
                reward["items"] = new JsonArray(new JsonObject
                {
                    ["_id"] = itemId,
                    ["_tpl"] = target,
                    ["upd"] = new JsonObject { ["StackObjectsCount"] = Math.Max(1, (int)value) },
                });
                break;
            case "TraderStanding":
            case "TraderUnlock":
            case "AssortmentUnlock":
                reward["target"] = ExtractTraderId(target);
                break;
        }

        return reward;
    }

    private static JsonArray GetConditionArray(JsonObject quest, string phase)
    {
        EnsureQuestShape(quest);
        var conditions = (JsonObject)quest["conditions"]!;
        if (conditions[phase] is not JsonArray array)
        {
            array = new JsonArray();
            conditions[phase] = array;
        }

        return array;
    }

    private static JsonArray GetRewardArray(JsonObject quest, string phase)
    {
        EnsureQuestShape(quest);
        var rewards = (JsonObject)quest["rewards"]!;
        if (rewards[phase] is not JsonArray array)
        {
            array = new JsonArray();
            rewards[phase] = array;
        }

        return array;
    }

    private static void EnsureQuestShape(JsonObject quest)
    {
        if (quest["conditions"] is not JsonObject conditions)
        {
            conditions = new JsonObject();
            quest["conditions"] = conditions;
        }

        foreach (var phase in new[] { "AvailableForStart", "AvailableForFinish", "Started", "Success", "Fail" })
        {
            if (conditions[phase] is not JsonArray)
            {
                conditions[phase] = new JsonArray();
            }
        }

        if (quest["rewards"] is not JsonObject rewards)
        {
            rewards = new JsonObject();
            quest["rewards"] = rewards;
        }

        foreach (var phase in new[] { "Started", "Success", "Fail" })
        {
            if (rewards[phase] is not JsonArray)
            {
                rewards[phase] = new JsonArray();
            }
        }
    }

    private string TraderName(string traderId)
    {
        return _traders.TryGetValue(ExtractTraderId(traderId), out var name) ? name : traderId;
    }

    private static string ExtractTraderId(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf('|', StringComparison.Ordinal);
        return separator >= 0 ? trimmed[..separator].Trim() : trimmed;
    }

    private static string NormalizeMapName(string? location)
    {
        return string.IsNullOrWhiteSpace(location) || location == "any" ? "Any" : location;
    }

    private static double? FindLevelRequirement(JsonObject quest)
    {
        var start = quest["conditions"]?["AvailableForStart"] as JsonArray;
        var level = start?.OfType<JsonObject>()
            .FirstOrDefault(c => string.Equals(GetString(c, "conditionType"), "Level", StringComparison.OrdinalIgnoreCase));
        return level is null ? null : GetDouble(level, "value");
    }

    private static string SummarizeCondition(JsonObject condition)
    {
        var type = GetString(condition, "conditionType") ?? string.Empty;
        var value = GetDouble(condition, "value");
        var target = condition["target"]?.ToJsonString(JsonOptions) ?? string.Empty;
        if (type == "CounterCreator")
        {
            var inner = condition["counter"]?["conditions"] as JsonArray;
            var first = inner?.OfType<JsonObject>().FirstOrDefault();
            return $"Counter {GetString(first, "conditionType")} target={first?["target"]?.ToJsonString()} value={value}";
        }

        if (type == "LeaveItemAtLocation")
        {
            return $"{type} target={target} value={value} zoneId={GetString(condition, "zoneId")} plantTime={GetDouble(condition, "plantTime")}";
        }

        return $"{type} target={target} value={value}";
    }

    private static string SummarizeReward(JsonObject reward)
    {
        var type = GetString(reward, "type") ?? string.Empty;
        var value = GetDouble(reward, "value");
        var target = GetString(reward, "target") ?? string.Empty;
        return $"{type} target={target} value={value}";
    }

    private static string? GetString(JsonObject? obj, string name)
    {
        if (obj is null || obj[name] is null)
        {
            return null;
        }

        try
        {
            return obj[name]!.GetValue<string>();
        }
        catch
        {
            return obj[name]!.ToJsonString();
        }
    }

    private static int? GetInt(JsonObject obj, string name)
    {
        var value = GetDouble(obj, name);
        return value.HasValue ? (int)value.Value : null;
    }

    private static double? GetDouble(JsonObject? obj, string name)
    {
        if (obj is null || obj[name] is null)
        {
            return null;
        }

        try
        {
            return obj[name]!.GetValue<double>();
        }
        catch
        {
            return double.TryParse(GetString(obj, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
    }

    private static bool? GetBool(JsonObject obj, string name)
    {
        if (obj[name] is null)
        {
            return null;
        }

        try
        {
            return obj[name]!.GetValue<bool>();
        }
        catch
        {
            return null;
        }
    }

    private static void SetString(JsonObject obj, string name, string value)
    {
        obj[name] = value;
    }

    private static void SetBool(JsonObject obj, string name, bool value)
    {
        obj[name] = value;
    }

    private static double ParseDouble(string text, double fallback)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : fallback;
    }

    private static (string ZoneId, double PlantTime) ParseZoneAndPlantTime(string text)
    {
        var parts = text.Split('|', 2, StringSplitOptions.TrimEntries);
        var zoneId = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : "zone_id";
        var plantTime = parts.Length > 1 ? ParseDouble(parts[1], 30) : 30;
        return (zoneId, plantTime);
    }

    private static IReadOnlyList<string> SplitList(string text, params string[] fallback)
    {
        var values = text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return values.Count > 0 ? values : fallback;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }
}

public sealed class QuestRecord
{
    public required string Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string QuestName { get; set; } = string.Empty;
    public string TraderId { get; set; } = string.Empty;
    public string TraderName { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double? MinLevel { get; set; }
    public string Source { get; set; } = string.Empty;
    public IReadOnlyList<string> Objectives { get; set; } = [];
    public required JsonObject Json { get; set; }
}

public sealed record TaskInfo(
    string Id,
    string Name,
    string TraderName,
    string MapName,
    string Type,
    int? MinPlayerLevel,
    IReadOnlyList<string> Objectives);

public sealed record RowText(string Text);

public sealed record ConditionRow(string Phase, string Type, string Summary, string Id);

public sealed record RewardRow(string Phase, string Type, string Summary, string Id);

public static class MongoIdGenerator
{
    private static readonly byte[] Machine = RandomNumberGenerator.GetBytes(3);
    private static readonly byte[] Process = RandomNumberGenerator.GetBytes(2);
    private static int _counter = RandomNumberGenerator.GetInt32(0, 0xFFFFFF);

    public static string NewId()
    {
        Span<byte> bytes = stackalloc byte[12];
        var timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bytes[0] = (byte)(timestamp >> 24);
        bytes[1] = (byte)(timestamp >> 16);
        bytes[2] = (byte)(timestamp >> 8);
        bytes[3] = (byte)timestamp;
        Machine.CopyTo(bytes[4..7]);
        Process.CopyTo(bytes[7..9]);
        var counter = Interlocked.Increment(ref _counter) & 0xFFFFFF;
        bytes[9] = (byte)(counter >> 16);
        bytes[10] = (byte)(counter >> 8);
        bytes[11] = (byte)counter;
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
