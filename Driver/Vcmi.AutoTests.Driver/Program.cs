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
            var testPack = new TestPack(Path.Combine(Configuration.VcmiRootDir, Configuration.MapsModBasePath));
            var maps = testPack.GetMaps();

            Console.WriteLine("{0} maps discovered.", maps.Count);

            if(Directory.Exists(Configuration.ResultDirectory))
            {
                Directory.Delete(Configuration.ResultDirectory, true);
            }

            Directory.CreateDirectory(Configuration.ResultDirectory);
            Directory.CreateDirectory(Configuration.ResultTempDirectory);

            File.WriteAllText(Configuration.ResultFileName, "<testrun>" + Environment.NewLine);

            foreach (var map in maps)
            {
                map.Run();
            }

            File.AppendAllText(Configuration.ResultFileName, "</testrun>");
        }
    }

    class Configuration
    {
        public const string VcmiRootDir = "d:/vcmi/build/bin/RelWithDebInfo";
        public const string SavesDir = "C:/Users/andri/Documents/My Games/vcmi";
        public const string ResultFileName = "result/result.xml";
        public const string MapsModBasePath = "Mods/Maps";
        public const string ResultTempDirectory = "result/current";
        public const string ResultDirectory = "result";
        public static readonly string[] TestArtifacts = {
            "VCMI_Client_log.txt",
            "VCMI_Server_log.txt"
        };

        public static string MakeMapPath(string relativePath)
        {
            return Path.Combine(VcmiRootDir, MapsModBasePath, relativePath);
        }
    }

    class TestPack
    {
        string path;

        public TestPack(string rootDirectory)
        {
            path = rootDirectory;
        }

        public List<TestMap> GetMaps()
        {
            var result = new List<TestMap>();
            var maps = Directory.GetFiles(
                path,
                "*.h3m",
                SearchOption.AllDirectories);

            return maps.Select(path => new TestMap(path)).ToList();
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
        private PlayerColor trackingPlayer;

        public TestMap(string path)
        {
            this.path = path;
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
                FileName = Path.Combine(Configuration.VcmiRootDir, "VCMI_client.exe"),
                Arguments = $@"--testmap ""MAPS/{Path.GetFileNameWithoutExtension(path)}"" --headless",
                WorkingDirectory = Configuration.VcmiRootDir,
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
                foreach (var artifact in Configuration.TestArtifacts)
                {
                    File.Copy(
                        Path.Combine(Configuration.SavesDir, artifact), 
                        Path.Combine(Configuration.ResultTempDirectory, artifact), 
                        overwrite: true);
                }

                ZipFile.CreateFromDirectory(Configuration.ResultTempDirectory, Path.Combine(Configuration.ResultDirectory, mapName + ".zip"));
            }

            File.AppendAllText(
                Configuration.ResultFileName,
                $@"  <test name=""{mapName}"" result=""{success}"" />{Environment.NewLine}");
        }

        private bool HasWon()
        {
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(100);
                try
                {
                    using (FileStream log = new FileStream(Path.Combine(Configuration.SavesDir, "VCMI_Client_log.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
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
