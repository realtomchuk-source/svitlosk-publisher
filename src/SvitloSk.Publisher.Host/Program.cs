using System;
using System.IO;
using System.Threading.Tasks;
using SvitloSk.Publisher.Core.Infrastructure;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Execution;
using SvitloSk.Publisher.Channels;

namespace SvitloSk.Publisher.Host;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("[Step 1] Reading configuration...");
            string envPath = @"C:\Users\ATom\Desktop\MyCoding\MyProgekts\svitlosk-specification\.env";
            string botToken = "";
            string channelId = "";
            string filePath = "";
            
            if (File.Exists(envPath))
            {
                var lines = await File.ReadAllLinesAsync(envPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("TELEGRAM_BOT_TOKEN=")) botToken = line.Substring("TELEGRAM_BOT_TOKEN=".Length).Trim();
                    if (line.StartsWith("TELEGRAM_CHANNEL_ID=")) channelId = line.Substring("TELEGRAM_CHANNEL_ID=".Length).Trim();
                    if (line.StartsWith("TODAY_TXT_PATH=")) filePath = line.Substring("TODAY_TXT_PATH=".Length).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(channelId)) channelId = "-1004394558011";
            if (string.IsNullOrWhiteSpace(filePath)) filePath = @"C:\Users\ATom\Desktop\MyCoding\MyProgekts\ParserAktualVidkl\starokostiantyniv-outages\data\tg_posts\today.txt";

            if (string.IsNullOrWhiteSpace(botToken))
            {
                Console.WriteLine("[Error] Telegram Bot Token is missing or .env file not found.");
                return;
            }

            Console.WriteLine("[Step 2] Reading today.txt...");
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[Error] Input file not found. Checked absolute path: {Path.GetFullPath(filePath)}");
                return;
            }

            var content = await File.ReadAllTextAsync(filePath);

            Console.WriteLine("[Step 3] Parsing input...");
            var parser = new TextPackageParser();
            var package = parser.Parse(content);
            Console.WriteLine($"         Parsed {package.PlannedDistricts.Count} planned districts.");

            Console.WriteLine("[Step 4] Building editorial decisions...");
            var decisionEngine = new EditorialDecisionEngine();
            var decisions = decisionEngine.Evaluate(package);
            Console.WriteLine($"         Generated {decisions.Count} decisions.");

            Console.WriteLine("[Step 5] Assembling edition...");
            var pubAssembler = new PublicationAssembler();
            var editionAssembler = new EditionAssembler(pubAssembler);
            var edition = editionAssembler.Assemble(package, decisions);
            Console.WriteLine($"         Assembled {edition.Publications.Count} publications for Edition.");

            Console.WriteLine("[Step 6] Connecting to Telegram...");
            var renderer = new MarkdownV2Renderer();
            var adapter = new TelegramAdapter(botToken, channelId, renderer);
            
            await adapter.PublishEditionAsync(edition);

            Console.WriteLine("[Step 7] Finished successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("\n[RUNTIME FATAL ERROR]");
            Console.WriteLine($"Type: {ex.GetType().FullName}");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine("StackTrace:");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
