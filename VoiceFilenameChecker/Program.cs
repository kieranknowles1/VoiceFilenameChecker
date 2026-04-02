using System.Diagnostics;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace VoiceFilenameChecker
{
    public static class StringExtensions
    {
        public static bool StartsWithIgnoreCase(this string source, string target)
        {
            return source.StartsWith(target, StringComparison.OrdinalIgnoreCase);
        }
    }

    public partial class Program
    {
        // TODO: How is length of quest/topic prefix determined?
        // Allow anything for now but may lead to false negatives
        [GeneratedRegex(@"(.+\.es[mpl])\\(.+)\\(\w+)_(\w*)_00(\w+)_(\d)\.fuz")]
        private static partial Regex FuzFileRegex();

        class Response
        {
            public required FormKey FormKey { get; init; }
            public required string VoiceType { get; init; }
            public required string Quest { get; init; }
            public required string? Topic { get; init; }
            public required int Index { get; init; }

            public static Response? ParseFileName(string filePath)
            {
                var match = FuzFileRegex().Match(filePath);
                if (!match.Success)
                    return null;

                var modKey = ModKey.FromNameAndExtension(match.Groups[1].Value);
                var voiceType = match.Groups[2].Value;
                var quest = match.Groups[3].Value;
                var topicRaw = match.Groups[4].Value;
                var formId = match.Groups[5].Value;
                var topic = topicRaw == "" ? null : topicRaw;
                var index = int.Parse(match.Groups[6].Value);

                var formKey = new FormKey(modKey, uint.Parse(formId, System.Globalization.NumberStyles.HexNumber));

                return new Response()
                {
                    FormKey = formKey,
                    VoiceType = voiceType,
                    Quest = quest,
                    Topic = topic,
                    Index = index,
                };
            }
        }

        static IEnumerable<string> WalkDirectory(string dir, string baseDir)
        {
            foreach (var subdirectory in Directory.GetDirectories(dir))
            {
                foreach (var file in WalkDirectory(subdirectory, baseDir))
                    yield return file;
            }
            foreach (var file in Directory.GetFiles(dir))
                yield return Path.GetRelativePath(baseDir, file);
        }

        public static async Task<int> Main(string[] args)
        {
            // TODO: Use Mutagen on its own
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "YourPatcher.esp")
                .Run(args);
        }

        static bool Check(string path, Response? response, ILinkCache linkCache)
        {
            bool ok = true;
            if (response == null)
            {
                Console.Error.WriteLine($"Bad file name {path}");
                return false;
            }

            //if (linkCache.TryResolve<IVoiceTypeGetter>(response.VoiceType, out var _))
            //{
            //    Console.Error.WriteLine($"  Bad voice type {response.VoiceType}");
            //    ok = false;
            //}

            if (!linkCache.TryResolveSimpleContext<IDialogResponsesGetter>(response.FormKey, out var responses))
            {
                Console.Error.WriteLine($"  No dialogue record");
                Console.Error.WriteLine($"Above file is {path}");
                return false;
            }

            var topic = responses.Parent?.Record as IDialogTopicGetter;
            Debug.Assert(topic != null);

            if (topic.EditorID != null && response.Topic != null)
            {
                if (!topic.EditorID.StartsWithIgnoreCase(response.Topic))
                {
                    Console.Error.WriteLine($"  Topic prefix mismatch");
                    ok = false;
                }
            }
            else if ((topic.EditorID == null) != (response.Topic == null))
            {
                Console.Error.WriteLine($"  Topic ID is null does not match file topic is null");
                ok = false;
            }

            var quest = linkCache.Resolve(topic.Quest);

            if (!quest.EditorID!.StartsWithIgnoreCase(response.Quest))
            {
                ok = false;
                Console.Error.WriteLine($"  Quest prefix mismatch");
            }

            if (!ok)
            {
                Console.Error.WriteLine($"Above file is {path}");
                Console.Error.WriteLine($"For response {quest.EditorID}_{topic.EditorID}");
            }
            return ok;
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var voiceDir = Path.Join(state.DataFolderPath, "Sound/Voice");

            uint good = 0;
            uint bad = 0;
            foreach (var file in WalkDirectory(voiceDir, voiceDir))
            {
                var ok = Check(file, Response.ParseFileName(file), state.LinkCache);
                if (ok)
                    good++;
                else
                    bad++;
            }
            Console.WriteLine($"{bad}/{good + bad} misnamed files");
        }
    }
}
