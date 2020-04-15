using CommandLine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace Vcmi.AutoTests.Driver
{
    class Program
    {
        static void Main(string[] args)
        {
            new Parser(s => s.CaseSensitive = false).ParseArguments<Configuration>(args).WithParsed(configuration =>
            {
                Console.WriteLine("VCMI automatic test driver.");

                var testPack = new TestPack(configuration);
                var maps = testPack.GetMaps();

                Console.WriteLine("{0} maps discovered.", maps.Count);

                if (Directory.Exists(configuration.ResultDirectory))
                {
                    Directory.Delete(configuration.ResultDirectory, true);
                }

                Directory.CreateDirectory(configuration.ResultDirectory);
                Directory.CreateDirectory(configuration.ResultTempDirectory);

                string resultsFileFullName = configuration.MakeResultsPath(configuration.ResultFileName);
                File.WriteAllText(resultsFileFullName, "<testrun>" + Environment.NewLine);

                foreach (var map in maps)
                {
                    map.Run();
                }

                File.AppendAllText(resultsFileFullName, "</testrun>");
            });
        }
    }

    public class Configuration
    {
        [Option(Required = true, HelpText = "Path to vcmi_client.exe")]
        public string VcmiRootDir { get; set; } = "d:/vcmi/build/bin/RelWithDebInfo";

        [Option(Required = true, HelpText = "Path to vcmi logs")]
        public string SavesDir { get; set; } = "C:/Users/andri/Documents/My Games/vcmi";

        [Option(Required = false, HelpText = "Output file name")]
        public string ResultFileName { get; set; } = "result.xml";

        [Option(Required = false, HelpText = "Path to maps relative vcmi root dir")]
        public string MapsModBasePath { get; set; } = "Mods/Maps";

        [Option(Required = false)]
        public string ResultDirectory { get; set; } = "result";

        [Option(Required = false)]
        public string VcmiClientName { get; set; } = "VCMI_client.exe";

        [Option(Required = false)]
        public string VcmiServerName { get; set; } = "VCMI_server.exe";
        
        public string[] TestArtifacts { get; set; } = {
            "VCMI_Client_log.txt",
            "VCMI_Server_log.txt"
        };

        public string ResultTempDirectory => MakeResultsPath("current");

        public string MakeMapPath(string relativePath)
        {
            return Path.Combine(VcmiRootDir, MapsModBasePath, relativePath);
        }

        public string MakeResultsPath(string relativePath)
        {
            return Path.Combine(ResultDirectory, relativePath);
        }

        public string MakeVcmiPath(string path)
        {
            return Path.Combine(VcmiRootDir, path);
        }
    }

    class TestPack
    {
        string path;
        private Configuration configuration;

        public TestPack(Configuration configuration)
        {
            this.configuration = configuration;
            this.path = Path.Combine(configuration.VcmiRootDir, configuration.MapsModBasePath);
        }

        public List<TestMap> GetMaps()
        {
            var result = new List<TestMap>();
            var maps = Directory.GetFiles(
                path,
                "*.h3m",
                SearchOption.AllDirectories);

            return maps.Select(path => new TestMap(path, configuration)).ToList();
        }
    }

    enum PlayerColor
    {
        Red = 0,

        Blue = 1,

        Green = 2
    }

    class TestMap
    {
        private string mapName;
        private string description;
        private string path;
        private readonly Configuration configuration;
        private PlayerColor trackingPlayer;

        public TestMap(string path, Configuration configuration)
        {
            this.path = path;
            this.configuration = configuration;
        }

        public void Run()
        {
            using (
                var stream = new BinaryReader(
                    new GZipStream(
                        File.OpenRead(path),
                        CompressionMode.Decompress)))
            {
                stream.ReadBytes(10);

                var mapNameSize = stream.ReadInt32();
                mapName = Encoding.ASCII.GetString(stream.ReadBytes(mapNameSize));

                var descriptionSize = stream.ReadInt32();
                description = Encoding.ASCII.GetString(stream.ReadBytes(descriptionSize));

                if (description.Contains("PLAYER:BLUE"))
                {
                    trackingPlayer = PlayerColor.Blue;
                }

                Console.WriteLine(mapName);
                Console.WriteLine(description);
            }

            ProcessStartInfo ps = new ProcessStartInfo
            {
                FileName = configuration.MakeVcmiPath(configuration.VcmiClientName),
                Arguments = $@"--testmap ""MAPS/{Path.GetFileNameWithoutExtension(path)}"" --headless",
                WorkingDirectory = configuration.VcmiRootDir,
                UseShellExecute = true
            };

            for (int i = 0; i < 3; i++)
            {
                Process p = Process.Start(ps);

                p.WaitForExit((int)TimeSpan.FromMinutes(1).TotalMilliseconds);

                if (!p.HasExited)
                {
                    p.Kill();
                }

                foreach (Process proc in Process.GetProcessesByName("VCMI_Server"))
                {
                    proc.Kill();
                    proc.WaitForExit();
                }

                if (p.ExitCode == 0)
                    break;
            }

            var success = HasWon();

            if (!success)
            {
                foreach (var artifact in configuration.TestArtifacts)
                {
                    File.Copy(
                        Path.Combine(configuration.SavesDir, artifact), 
                        Path.Combine(configuration.ResultTempDirectory, artifact), 
                        overwrite: true);
                }

                ZipFile.CreateFromDirectory(configuration.ResultTempDirectory, Path.Combine(configuration.ResultDirectory, mapName + ".zip"));
            }

            File.AppendAllText(
                configuration.MakeResultsPath(configuration.ResultFileName),
                $@"  <test name=""{mapName}"" result=""{success}"" />{Environment.NewLine}");
        }

        private bool HasWon()
        {
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(100);
                try
                {
                    using (FileStream log = new FileStream(Path.Combine(configuration.SavesDir, "VCMI_Client_log.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        log.Seek(-10000, SeekOrigin.End);

                        var playerString = $"{(int)trackingPlayer} ({trackingPlayer.ToString().ToLowerInvariant()})";

                        using (var reader = new StreamReader(log))
                        {
                            while (!reader.EndOfStream)
                            {
                                var line = reader.ReadLine();

                                if (string.IsNullOrEmpty(line))
                                    continue;

                                if (line.EndsWith($"VCAI: Player {playerString} lost. It's me. What a disappointment! :("))
                                    return false;

                                if (line.EndsWith($"VCAI: Player {playerString} won. I won! Incredible!"))
                                    return true;
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            return false;
        }
    }
}
