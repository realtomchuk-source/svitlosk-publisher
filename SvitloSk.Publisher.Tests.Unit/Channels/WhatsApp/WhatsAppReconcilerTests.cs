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
using SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;
using SvitloSk.Publisher.Infrastructure.Persistence;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Channels.WhatsApp;

public class WhatsAppReconcilerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryPath;
    private readonly string _journalPath;
    private readonly IRegistryStore _registryStore;
    private readonly WhatsAppPostJournalStore _journalStore;
    private readonly ContentHashCalculator _hashCalculator;

    public WhatsAppReconcilerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wa_reconciler_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _registryPath = Path.Combine(_tempDir, "whatsapp_registry.json");
        _journalPath = Path.Combine(_tempDir, "whatsapp_post_journal.json");

        var atomicWriter = new FileSystemAtomicWriter();
        _registryStore = new JsonRegistryStore(atomicWriter);
        _journalStore = new WhatsAppPostJournalStore(atomicWriter);
        _hashCalculator = new ContentHashCalculator();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    private WhatsAppReconciler CreateSut(IWhatsAppAdapter adapter) =>
        new WhatsAppReconciler(
            adapter,
            _registryStore,
            _journalStore,
            _hashCalculator,
            new StubGraphicAssembly(),
            new StubGraphicRasterizer(),
            new NoopDelayProvider(),
            postDelayMs: 0);

    private sealed class StubGraphicAssembly : IGraphicAssembly
    {
        public byte[] AssembleSvg(GraphicInputPackage package) =>
            new byte[] { 1, 2, 3 };
    }

    private sealed class StubGraphicRasterizer : IGraphicRasterizer
    {
        public byte[] RasterizeSvgToPng(byte[] svgBytes, int width = 800, int height = 1000) =>
            new byte[] { 0x89, 0x50, 0x4E, 0x47 };
    }

    private sealed class NoopDelayProvider : IDelayProvider
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeWhatsAppReconcilerAdapter : IWhatsAppAdapter
    {
        public WhatsAppTailStateResult TailState { get; set; } = new(true, Array.Empty<WhatsAppTailMessage>());
        public Func<string, string, WhatsAppDispatchResult>? OnUpdate { get; set; }
        public List<string> PublishedMarkers { get; } = new();
        public List<string> DeletedIds { get; } = new();
        public List<string> CallOrder { get; } = new();

        public Task<WhatsAppTailStateResult> GetTailStateAsync(string channelOrChatId, string? todayDdMm = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(TailState);

        public Task<WhatsAppDispatchResult> SendTextMessageAsync(string channelOrChatId, string text, CancellationToken cancellationToken = default)
        {
            WhatsAppContentFormatter.TryExtractMarkerFromText(text, out var parsed);
            string marker = parsed?.Marker ?? "NO_MARKER";
            PublishedMarkers.Add(marker);
            CallOrder.Add($"PUB:{marker}");
            return Task.FromResult(new WhatsAppDispatchResult(true, $"msg_{marker}", null, false, null, "created"));
        }

        public Task<WhatsAppDispatchResult> SendMediaMessageAsync(string channelOrChatId, string caption, byte[] mediaBytes, string mimeType = "image/png", CancellationToken cancellationToken = default)
        {
            WhatsAppContentFormatter.TryExtractMarkerFromText(caption, out var parsed);
            string marker = parsed?.Marker ?? "NO_MARKER";
            PublishedMarkers.Add(marker);
            CallOrder.Add($"PUB:{marker}");
            return Task.FromResult(new WhatsAppDispatchResult(true, $"msg_{marker}", null, false, null, "created"));
        }

        public Task<WhatsAppDispatchResult> UpdateTextMessageAsync(string channelOrChatId, string messageId, string text, CancellationToken cancellationToken = default)
        {
            WhatsAppContentFormatter.TryExtractMarkerFromText(text, out var parsed);
            string marker = parsed?.Marker ?? "NO_MARKER";
            CallOrder.Add($"UPD:{marker}");
            if (OnUpdate != null)
                return Task.FromResult(OnUpdate(messageId, text));
            return Task.FromResult(new WhatsAppDispatchResult(true, messageId, null, false, null, "updated"));
        }

        public Task<WhatsAppDispatchResult> DeleteMessageAsync(string channelOrChatId, string messageId, CancellationToken cancellationToken = default)
        {
            DeletedIds.Add(messageId);
            CallOrder.Add($"DEL:{messageId}");
            return Task.FromResult(new WhatsAppDispatchResult(true, messageId, null, false, null, "deleted"));
        }

        public Task<WhatsAppDispatchResult> CheckChannelAccessAsync(string channelOrChatId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WhatsAppDispatchResult(true, channelOrChatId, "OK"));

        public Task<WhatsAppDispatchResult> SweepObsoleteForecastsAsync(string channelOrChatId, string todayDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WhatsAppDispatchResult(true, channelOrChatId));
    }

    private static EditorialInput BuildSampleEditorialInput(string editionDate, bool withTomorrow = false)
    {
        var packages = new List<InputTerritoryPackage>
        {
            new InputTerritoryPackage(
                "journal_header",
                "*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*\n26 жовтня",
                new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                IsPersistent: true),
            new InputTerritoryPackage(
                "starokostiantyniv",
                "*м. СТАРОКОСТЯНТИНІВ*\n\n*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ*\n*09:00 – 17:00*\n- вул. Миру: 1–10",
                 null,
                IsPersistent: true),
            new InputTerritoryPackage(
                "pashkovetskyi",
                "*ПАШКОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ*\n\n*с. Пашківці* • *08:30 – 16:30*\n- вул. Центральна: 1–5",
                null,
                IsPersistent: true),
            new InputTerritoryPackage(
                "system_status",
                "Останнє оновлення журналу: *14:00*\nСтан моніторингу: активний",
                null,
                IsPersistent: false)
        };

        if (withTomorrow)
        {
            packages.Insert(3, new InputTerritoryPackage(
                "tomorrow_separator",
                "*ПРОГНОЗ НА ЗАВТРА*",
                new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                IsPersistent: false));
            packages.Insert(4, new InputTerritoryPackage(
                "tomorrow_pashkovetskyi",
                "*ПРОГНОЗ НА ЗАВТРА* • *27.10.2026*\n\n*ПАШКОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ*\n- вул. Центральна: 1–5",
                null,
                IsPersistent: false));
        }

        var queues = new List<QueueSchedule>();
        for (int q = 1; q <= 6; q++)
        {
            var sq1Intervals = q == 1
                ? new List<GraphicInterval> { new GraphicInterval("09:00", "12:00", "Restricted") }
                : new List<GraphicInterval>();
            var subqueues = new List<SubqueueSchedule>
            {
                new SubqueueSchedule($"Черга {q}.1", sq1Intervals),
                new SubqueueSchedule($"Черга {q}.2", new List<GraphicInterval>())
            };
            queues.Add(new QueueSchedule($"Черга {q}", subqueues));
        }

        var graphicPkg = new GraphicInputPackage(
            Metadata: new GraphicMetadata("pkg-1", "2026-10-26 14:00", editionDate, "TEST"),
            TerritorialScope: "Старокостянтинівська МТГ",
            Queues: queues
        );

        return new EditorialInput(
            EditionDate: editionDate,
            Packages: packages,
            TomorrowForecastAvailable: withTomorrow,
            GraphicPackage: graphicPkg,
            SourceQueryTime: new DateTime(2026, 10, 26, 14, 0, 0)
        );
    }

    [Fact]
    public async Task ReconcileAsync_EmptyChannel_PublishesAllPostsInStrictSequentialOrderWithMarkers()
    {
        string todayIso = KyivClock.TodayIso;
        string todayDdMm = WhatsAppContentFormatter.FormatMarkerDate(todayIso);
        string tomorrowDdMm = WhatsAppContentFormatter.FormatMarkerDate(DateTime.Parse(todayIso).AddDays(1).ToString("yyyy-MM-dd"));
        var input = BuildSampleEditorialInput(todayIso, withTomorrow: true);

        var adapter = new FakeWhatsAppReconcilerAdapter();
        var sut = CreateSut(adapter);
        var result = await sut.ReconcileAsync("0029Vb96XUUIN9ixA88Umz3Y", _registryPath, _journalPath, input);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[]
        {
            $"T · {todayDdMm} · 00",
            $"T · {todayDdMm} · 01",
            $"T · {todayDdMm} · 02",
            $"F · {tomorrowDdMm} · 00",
            $"F · {tomorrowDdMm} · 01",
            $"G · {todayDdMm}",
            $"S · {todayDdMm}"
        }, adapter.PublishedMarkers);
    }

    [Fact]
    public async Task ReconcileAsync_WhenChannelMatchesDesiredState_IsIdempotentZeroMutations()
    {
        string todayIso = KyivClock.TodayIso;
        string todayDdMm = WhatsAppContentFormatter.FormatMarkerDate(todayIso);
        var input = BuildSampleEditorialInput(todayIso);

        var t00Clean = input.Packages[0].Content;
        var t01Clean = input.Packages[1].Content;
        var t02Clean = input.Packages[2].Content;
        var gClean = WhatsAppContentFormatter.RenderGraphicScheduleCaption(todayIso);
        var sClean = input.Packages[3].Content;

        // Pre-seed journal with matching graphic hash so G is recognized as unchanged
        string gHash = _hashCalculator.ComputeGraphicHash(input.GraphicPackage!);
        await _journalStore.SaveWithRotationAsync(_journalPath, new WhatsAppPostJournalModel(
            1,
            todayIso,
            KyivClock.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            new List<WhatsAppJournalEntry>
            {
                new($"G · {todayDdMm}", "G", todayIso, null, "Старокостянтинівська МТГ", "mg", gHash, "SENT", 1, "2026-10-26 14:00:00")
            }));

        var domMessages = new List<WhatsAppTailMessage>
        {
            new(0, "m0", null, $"T · {todayDdMm} · 00", "T", todayDdMm, 0, "*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*", t00Clean, true),
            new(1, "m1", null, $"T · {todayDdMm} · 01", "T", todayDdMm, 1, "*м. СТАРОКОСТЯНТИНІВ*", t01Clean, false),
            new(2, "m2", null, $"T · {todayDdMm} · 02", "T", todayDdMm, 2, "*ПАШКОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ*", t02Clean, false),
            new(3, "mg", null, $"G · {todayDdMm}", "G", todayDdMm, null, "*ГРАФІК ПОГОДИННИХ ВІДКЛЮЧЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*", gClean, true),
            new(4, "ms", null, $"S · {todayDdMm}", "S", todayDdMm, null, "Останнє оновлення журналу: *14:00*", sClean, false)
        };

        var adapter = new FakeWhatsAppReconcilerAdapter
        {
            TailState = new WhatsAppTailStateResult(true, domMessages)
        };

        var sut = CreateSut(adapter);
        var result = await sut.ReconcileAsync("0029Vb96XUUIN9ixA88Umz3Y", _registryPath, _journalPath, input);

        Assert.True(result.IsSuccess);
        Assert.Empty(adapter.CallOrder);
        Assert.All(result.Results, r => Assert.Equal("NoAction", r.DecisionResult));
    }

    [Fact]
    public async Task ReconcileAsync_VariantATailRebuild_WhenT01EditWindowExpired_DeletesTailFromT01AndRepublishes()
    {
        string todayIso = KyivClock.TodayIso;
        string todayDdMm = WhatsAppContentFormatter.FormatMarkerDate(todayIso);
        var input = BuildSampleEditorialInput(todayIso);

        var t00Clean = input.Packages[0].Content;
        var t01OldClean = "*м. СТАРОКОСТЯНТИНІВ*\n\n*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ*\n*09:00 – 12:00*\n- вул. Стара: 1";
        var t02Clean = input.Packages[2].Content;
        var gClean = WhatsAppContentFormatter.RenderGraphicScheduleCaption(todayIso);
        var sClean = input.Packages[3].Content;

        var domMessages = new List<WhatsAppTailMessage>
        {
            new(0, "m0", null, $"T · {todayDdMm} · 00", "T", todayDdMm, 0, "*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*", t00Clean, true),
            new(1, "m1", null, $"T · {todayDdMm} · 01", "T", todayDdMm, 1, "*м. СТАРОКОСТЯНТИНІВ*", t01OldClean, false),
            new(2, "m2", null, $"T · {todayDdMm} · 02", "T", todayDdMm, 2, "*ПАШКОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ*", t02Clean, false),
            new(3, "mg", null, $"G · {todayDdMm}", "G", todayDdMm, null, "*ГРАФІК ПОГОДИННИХ ВІДКЛЮЧЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*", gClean, true),
            new(4, "ms", null, $"S · {todayDdMm}", "S", todayDdMm, null, "Останнє оновлення журналу: *14:00*", sClean, false)
        };

        var adapter = new FakeWhatsAppReconcilerAdapter
        {
            TailState = new WhatsAppTailStateResult(true, domMessages),
            OnUpdate = (msgId, _) => new WhatsAppDispatchResult(false, msgId, "15-minute window expired", false, null, "windowExpired")
        };

        var sut = CreateSut(adapter);
        var result = await sut.ReconcileAsync("0029Vb96XUUIN9ixA88Umz3Y", _registryPath, _journalPath, input);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("m0", adapter.DeletedIds);
        Assert.Contains("m1", adapter.DeletedIds);
        Assert.Contains("m2", adapter.DeletedIds);
        Assert.Contains("mg", adapter.DeletedIds);
        Assert.Contains("ms", adapter.DeletedIds);

        Assert.Equal(new[]
        {
            $"T · {todayDdMm} · 01",
            $"T · {todayDdMm} · 02",
            $"G · {todayDdMm}",
            $"S · {todayDdMm}"
        }, adapter.PublishedMarkers);
    }

    [Fact]
    public async Task ReconcileAsync_AnchorBannerProtection_WhenT00EditWindowExpired_KeepsT00WithoutRebuildingWholeDay()
    {
        string todayIso = KyivClock.TodayIso;
        string todayDdMm = WhatsAppContentFormatter.FormatMarkerDate(todayIso);
        var input = BuildSampleEditorialInput(todayIso);

        var t00OldClean = "*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*\n26 жовтня\n*Планові знеструмлення:* м. Старокостянтинів";
        var t01Clean = input.Packages[1].Content;
        var t02Clean = input.Packages[2].Content;
        var gClean = WhatsAppContentFormatter.RenderGraphicScheduleCaption(todayIso);
        var sClean = input.Packages[3].Content;

        string gHash = _hashCalculator.ComputeGraphicHash(input.GraphicPackage!);
        await _journalStore.SaveWithRotationAsync(_journalPath, new WhatsAppPostJournalModel(
            1,
            todayIso,
            KyivClock.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            new List<WhatsAppJournalEntry>
            {
                new($"G · {todayDdMm}", "G", todayIso, null, "Старокостянтинівська МТГ", "mg", gHash, "SENT", 1, "2026-10-26 14:00:00")
            }));

        var domMessages = new List<WhatsAppTailMessage>
        {
            new(0, "m0", null, $"T · {todayDdMm} · 00", "T", todayDdMm, 0, "*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*", t00OldClean, true),
            new(1, "m1", null, $"T · {todayDdMm} · 01", "T", todayDdMm, 1, "*м. СТАРОКОСТЯНТИНІВ*", t01Clean, false),
            new(2, "m2", null, $"T · {todayDdMm} · 02", "T", todayDdMm, 2, "*ПАШКОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ*", t02Clean, false),
            new(3, "mg", null, $"G · {todayDdMm}", "G", todayDdMm, null, "*ГРАФІК ПОГОДИННИХ ВІДКЛЮЧЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*", gClean, true),
            new(4, "ms", null, $"S · {todayDdMm}", "S", todayDdMm, null, "Останнє оновлення журналу: *14:00*", sClean, false)
        };

        var adapter = new FakeWhatsAppReconcilerAdapter
        {
            TailState = new WhatsAppTailStateResult(true, domMessages),
            OnUpdate = (msgId, _) => new WhatsAppDispatchResult(false, msgId, "windowExpired", false, null, "windowExpired")
        };

        var sut = CreateSut(adapter);
        var result = await sut.ReconcileAsync("0029Vb96XUUIN9ixA88Umz3Y", _registryPath, _journalPath, input);

        Assert.True(result.IsSuccess);
        Assert.Empty(adapter.DeletedIds);
        Assert.Empty(adapter.PublishedMarkers);
    }

    [Fact]
    public async Task ReconcileAsync_DeletesObsoleteForecastBeforePublishingToday()
    {
        string todayIso = KyivClock.TodayIso;
        string todayDdMm = WhatsAppContentFormatter.FormatMarkerDate(todayIso);
        var input = BuildSampleEditorialInput(todayIso);

        var domMessages = new List<WhatsAppTailMessage>
        {
            new(0, "f0", null, $"F · {todayDdMm} · 00", "F", todayDdMm, 0, "*ПРОГНОЗ НА ЗАВТРА*", "прогноз", true),
            new(1, "f1", null, $"F · {todayDdMm} · 01", "F", todayDdMm, 1, "*м. СТАРОКОСТЯНТИНІВ*", "прогноз 1", false)
        };

        var adapter = new FakeWhatsAppReconcilerAdapter
        {
            TailState = new WhatsAppTailStateResult(true, domMessages)
        };

        var sut = CreateSut(adapter);
        var result = await sut.ReconcileAsync("0029Vb96XUUIN9ixA88Umz3Y", _registryPath, _journalPath, input);

        Assert.True(result.IsSuccess);
        Assert.Equal("DEL:f0", adapter.CallOrder[0]);
        Assert.Equal("DEL:f1", adapter.CallOrder[1]);
        Assert.StartsWith("PUB:T ·", adapter.CallOrder[2]);
    }

    [Fact]
    public async Task ReconcileAsync_FailClosed_WhenGetTailStateFails_PerformsZeroMutations()
    {
        string todayIso = KyivClock.TodayIso;
        var input = BuildSampleEditorialInput(todayIso);

        var adapter = new FakeWhatsAppReconcilerAdapter
        {
            TailState = new WhatsAppTailStateResult(false, Array.Empty<WhatsAppTailMessage>(), "Channel not active in #main")
        };

        var sut = CreateSut(adapter);
        var result = await sut.ReconcileAsync("0029Vb96XUUIN9ixA88Umz3Y", _registryPath, _journalPath, input);

        Assert.False(result.IsSuccess);
        Assert.Contains("Fail-Closed", result.FatalErrorDescription);
        Assert.Empty(adapter.CallOrder);
    }
}
