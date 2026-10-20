using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Facebook;
using SvitloSk.Publisher.Infrastructure.Channels.Telegram;
using SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Orchestration;

public class TomorrowOrderingTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _registryPath;

    private class FakeDelayProvider : IDelayProvider
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public TomorrowOrderingTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SvitloSk_TomOrderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _registryPath = Path.Combine(_testDirectory, "registry.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task TC_Telegram_WhenNewTodayTerritoryCreatedAfterTomorrow_DeletesAndRecreatesTomorrowBelowToday()
    {
        var store = new PublisherOrchestratorTests.FakeRegistryStore();
        var git = new PublisherOrchestratorTests.FakeGitTransport();
        var adapter = new PublisherOrchestratorTests.FakeTelegramAdapter();
        var calc = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var dispatcher = new SequentialDispatcher(adapter, new FakeDelayProvider());
        var orchestrator = new PublisherOrchestrator(store, git, calc, decisionEngine, dispatcher, parser, transformer);

        string headerText = "<b>Планові знеструмлення:</b> м. Старокостянтинів";
        string staroText = "<b>м. Старокостянтинів</b>\n<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ (09:00–17:00)</b>\n- вул. Миру, 1";
        string tomSepText = "<b>Планові знеструмлення:</b> м. Старокостянтинів";
        string tomStaroText = "<b>м. Старокостянтинів</b>\n<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ (08:00–16:00)</b>\n- вул. Франка, 2";
        string newEmergencyOkruhText = "<b>Великочернятинський старостинський округ</b>\n<blockquote><b>АВАРІЙНІ ЗНЕСТРУМЛЕННЯ (14:04–17:00)</b>\n<b>с. Малий Чернятин</b>\n- вул. Центральна</blockquote>";

        store.CurrentModel = new RegistryModel(
            SchemaVersion: 1,
            EditionDate: "2026-10-20",
            Status: "PLANNED",
            Publications: new List<RegistryPublicationRecord>
            {
                new RegistryPublicationRecord(Guid.NewGuid(), "journal_header", 658, calc.ComputeHash(headerText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "starokostiantyniv", 659, calc.ComputeHash(staroText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "tomorrow_separator", 664, calc.ComputeHash(tomSepText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "tomorrow_starokostiantyniv", 665, calc.ComputeHash(tomStaroText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "system_status", 670, "old-status-hash", "SENT", "Text")
            }
        );

        var input = new EditorialInput(
            EditionDate: "2026-10-20",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("journal_header", headerText, null, true),
                new InputTerritoryPackage("starokostiantyniv", staroText, null, true),
                new InputTerritoryPackage("bereznenskyi", newEmergencyOkruhText, null, true),
                new InputTerritoryPackage("tomorrow_separator", tomSepText, null, false),
                new InputTerritoryPackage("tomorrow_starokostiantyniv", tomStaroText, null, false)
            },
            TomorrowForecastAvailable: true
        );

        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);

        Assert.True(result.IsSuccess);

        // 1. New today territory bereznenskyi is created
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "bereznenskyi" && r.DecisionResult == "Create");

        // 2. Existing tomorrow posts (664, 665) are deleted and recreated AFTER bereznenskyi
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_separator" && r.DecisionResult == "Delete");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_starokostiantyniv" && r.DecisionResult == "Delete");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_separator" && r.DecisionResult == "Create");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_starokostiantyniv" && r.DecisionResult == "Create");

        // Verify execution order: bereznenskyi Create -> tomorrow Delete -> tomorrow Create -> system_status Delete & Create
        var resultList = result.Results.ToList();
        int idxTodayCreate = resultList.FindIndex(r => r.TerritoryIdentifier == "bereznenskyi" && r.DecisionResult == "Create");
        int idxTomDelete = resultList.FindIndex(r => r.TerritoryIdentifier == "tomorrow_separator" && r.DecisionResult == "Delete");
        int idxTomCreate = resultList.FindIndex(r => r.TerritoryIdentifier == "tomorrow_separator" && r.DecisionResult == "Create");
        int idxStatusCreate = resultList.FindIndex(r => r.TerritoryIdentifier == "system_status" && r.DecisionResult == "Create");

        Assert.True(idxTodayCreate >= 0);
        Assert.True(idxTomDelete > idxTodayCreate);
        Assert.True(idxTomCreate > idxTomDelete);
        Assert.True(idxStatusCreate > idxTomCreate);
    }

    [Fact]
    public async Task TC_Telegram_SelfHeals_WhenTodayPostAlreadyHasMessageIdGreaterThanTomorrow()
    {
        var store = new PublisherOrchestratorTests.FakeRegistryStore();
        var git = new PublisherOrchestratorTests.FakeGitTransport();
        var adapter = new PublisherOrchestratorTests.FakeTelegramAdapter();
        var calc = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var dispatcher = new SequentialDispatcher(adapter, new FakeDelayProvider());
        var orchestrator = new PublisherOrchestrator(store, git, calc, decisionEngine, dispatcher, parser, transformer);

        string headerText = "<b>Планові знеструмлення:</b> м. Старокостянтинів";
        string staroText = "<b>м. Старокостянтинів</b>";
        string okruhText = "<b>Березненський старостинський округ</b>";
        string tomSepText = "<b>Планові знеструмлення:</b> м. Старокостянтинів";
        string tomStaroText = "<b>м. Старокостянтинів</b>";

        // Registry mirrors the exact misordered state: tomorrow has msgId 664, 665, while bereznenskyi has msgId 671!
        store.CurrentModel = new RegistryModel(
            SchemaVersion: 1,
            EditionDate: "2026-10-20",
            Status: "PLANNED",
            Publications: new List<RegistryPublicationRecord>
            {
                new RegistryPublicationRecord(Guid.NewGuid(), "journal_header", 658, calc.ComputeHash(headerText, null), "UPDATED", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "starokostiantyniv", 659, calc.ComputeHash(staroText, null), "UPDATED", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "bereznenskyi", 671, calc.ComputeHash(okruhText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "system_status", 672, "old-status-hash", "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "tomorrow_separator", 664, calc.ComputeHash(tomSepText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "tomorrow_starokostiantyniv", 665, calc.ComputeHash(tomStaroText, null), "SENT", "Text")
            }
        );

        // Even when no content has changed (all hashes match), self-healing must detect 671 > 664 and reposition tomorrow!
        var input = new EditorialInput(
            EditionDate: "2026-10-20",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("journal_header", headerText, null, true),
                new InputTerritoryPackage("starokostiantyniv", staroText, null, true),
                new InputTerritoryPackage("bereznenskyi", okruhText, null, true),
                new InputTerritoryPackage("tomorrow_separator", tomSepText, null, false),
                new InputTerritoryPackage("tomorrow_starokostiantyniv", tomStaroText, null, false)
            },
            TomorrowForecastAvailable: true
        );

        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_separator" && r.DecisionResult == "Delete");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_starokostiantyniv" && r.DecisionResult == "Delete");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_separator" && r.DecisionResult == "Create");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_starokostiantyniv" && r.DecisionResult == "Create");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "system_status" && r.DecisionResult == "Delete");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "system_status" && r.DecisionResult == "Create");
    }

    [Fact]
    public async Task TC_WhatsApp_WhenNewTodayTerritoryCreatedAfterTomorrow_DeletesAndRecreatesTomorrowBelowToday()
    {
        var store = new PublisherOrchestratorTests.FakeRegistryStore();
        var git = new PublisherOrchestratorTests.FakeGitTransport();
        var dryRunAdapter = new WhatsAppDryRunAdapter();
        var whatsAppPipeline = new WhatsAppPipeline(dryRunAdapter, "12036304@newsletter");
        var calc = new ContentHashCalculator();
        var orchestrator = new PublisherOrchestrator(
            store,
            git,
            calc,
            new EditorialDecisionEngine(),
            whatsAppPipeline,
            new OutageFeedParser(),
            new EditorialContentTransformer()
        );

        string headerText = "<b>Планові знеструмлення:</b> м. Старокостянтинів";
        string staroText = "<b>м. Старокостянтинів</b>";
        string okruhText = "<b>Березненський старостинський округ</b>";
        string tomSepText = "<b>Планові знеструмлення:</b> м. Старокостянтинів";
        string tomStaroText = "<b>м. Старокостянтинів</b>";

        store.CurrentModel = new RegistryModel(
            SchemaVersion: 1,
            EditionDate: "2026-10-20",
            Status: "PLANNED",
            Publications: new List<RegistryPublicationRecord>
            {
                new RegistryPublicationRecord(Guid.NewGuid(), "journal_header", "wa_header_1", calc.ComputeHash(headerText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "starokostiantyniv", "wa_staro_1", calc.ComputeHash(staroText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "tomorrow_separator", "wa_tom_sep_1", calc.ComputeHash(tomSepText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "tomorrow_starokostiantyniv", "wa_tom_staro_1", calc.ComputeHash(tomStaroText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "system_status", "wa_status_1", "old-status-hash", "SENT", "Text")
            }
        );

        var input = new EditorialInput(
            EditionDate: "2026-10-20",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("journal_header", headerText, null, true),
                new InputTerritoryPackage("starokostiantyniv", staroText, null, true),
                new InputTerritoryPackage("bereznenskyi", okruhText, null, true),
                new InputTerritoryPackage("tomorrow_separator", tomSepText, null, false),
                new InputTerritoryPackage("tomorrow_starokostiantyniv", tomStaroText, null, false)
            },
            TomorrowForecastAvailable: true
        );

        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "12036304@newsletter", input);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "bereznenskyi" && r.DecisionResult == "Create");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_separator" && r.DecisionResult == "Delete");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_starokostiantyniv" && r.DecisionResult == "Delete");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_separator" && r.DecisionResult == "Create");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "tomorrow_starokostiantyniv" && r.DecisionResult == "Create");

        // Verify saved registry order: Today -> Tomorrow -> System Status
        Assert.NotNull(store.CurrentModel);
        var savedIds = store.CurrentModel.Publications.Select(p => p.TerritoryId).ToList();
        int idxOkruh = savedIds.IndexOf("bereznenskyi");
        int idxTomSep = savedIds.IndexOf("tomorrow_separator");
        int idxStatus = savedIds.IndexOf("system_status");

        Assert.True(idxOkruh >= 0);
        Assert.True(idxTomSep > idxOkruh);
        Assert.True(idxStatus > idxTomSep);
    }

    [Fact]
    public async Task TC_Facebook_WhenFbPlannedRecreatedOrCreated_DeletesAndRecreatesFbTomorrowAfterFbPlanned()
    {
        var store = new PublisherOrchestratorTests.FakeRegistryStore();
        var git = new PublisherOrchestratorTests.FakeGitTransport();
        var dryRunAdapter = new FacebookDryRunAdapter();
        var fbPipeline = new FacebookPipeline(dryRunAdapter, "test_page_123");
        var calc = new ContentHashCalculator();
        var orchestrator = new PublisherOrchestrator(
            store,
            git,
            calc,
            new EditorialDecisionEngine(),
            fbPipeline,
            new OutageFeedParser(),
            new EditorialContentTransformer()
        );

        string noOutagesText = "<b>Планові знеструмлення:</b> відсутні\n<b>Аварійні знеструмлення:</b> відсутні";
        string activeOutagesText = "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n20.10.2026\n\nПланові знеструмлення: м. Старокостянтинів\nАварійні знеструмлення: с. Малий Чернятин";
        string fbTomorrowText = "ПРОГНОЗ ЗНЕСТРУМЛЕНЬ НА ЗАВТРА\n21.10.2026\nм. Старокостянтинів";

        store.CurrentModel = new RegistryModel(
            SchemaVersion: 1,
            EditionDate: "2026-10-20",
            Status: "PLANNED",
            Publications: new List<RegistryPublicationRecord>
            {
                new RegistryPublicationRecord(Guid.NewGuid(), "fb_planned", "fb_post_planned_1", calc.ComputeHash(noOutagesText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "fb_tomorrow", "fb_post_tomorrow_1", calc.ComputeHash(fbTomorrowText, null), "SENT", "Text"),
                new RegistryPublicationRecord(Guid.NewGuid(), "system_status", "fb_virtual_system_status", "status-hash", "SENT", "Text")
            }
        );

        var input = new EditorialInput(
            EditionDate: "2026-10-20",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("fb_planned", activeOutagesText, null, true),
                new InputTerritoryPackage("fb_tomorrow", fbTomorrowText, null, false)
            },
            TomorrowForecastAvailable: true
        );

        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "test_page_123", input);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "fb_tomorrow" && r.DecisionResult == "Delete");
        Assert.Contains(result.Results, r => r.TerritoryIdentifier == "fb_tomorrow" && r.DecisionResult == "Create");
    }
}
