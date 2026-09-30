using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

return ReleaseTool.Run(args);

internal static class ReleaseTool
{
    private static readonly Regex TagPattern=new("^v(?<version>0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?<beta>-beta\\.(0|[1-9][0-9]*))?$",RegexOptions.CultureInvariant);
    private static readonly DateTimeOffset ArchiveTime=new(2000,1,1,0,0,0,TimeSpan.Zero);

    public static int Run(string[] args)
    {
        try
        {
            if(args is ["self-test"])return SelfTest();
            if(args.Length<3||args.Length%2!=1)throw new ArgumentException("Expected create or verify with named options.");
            var options=new Dictionary<string,string>(StringComparer.Ordinal);
            for(var i=1;i<args.Length;i+=2)
                if(!args[i].StartsWith("--",StringComparison.Ordinal)||!options.TryAdd(args[i],args[i+1])||
                    string.IsNullOrWhiteSpace(args[i+1]))throw new ArgumentException("Malformed or duplicate release option.");
            if(args[0]=="create"&&options.Count==6)
                Create(options["--tag"],options["--project"],options["--package"],options["--output"],options["--repository"],options["--notes-url"]);
            else if(args[0]=="verify"&&options.Count==4)
                Verify(options["--tag"],options["--project"],options["--output"],options["--repository"]);
            else throw new ArgumentException("Expected create (--tag --project --package --output --repository --notes-url) or verify (--tag --project --output --repository).");
            return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void Create(string tag,string projectPath,string packagePath,string outputPath,string repository,string notesUrl)
    {
        var match=TagPattern.Match(tag);
        if(!match.Success)throw new InvalidDataException($"Tag '{tag}' is not a supported stable or beta SemVer tag.");
        var version=tag[1..];
        var project=XDocument.Load(projectPath);
        var projectVersion=project.Descendants("Version").Select(x=>x.Value.Trim()).SingleOrDefault()
            ??throw new InvalidDataException("The application project must contain exactly one Version element.");
        if(version!=projectVersion)throw new InvalidDataException($"Tag version {version} does not match application version {projectVersion}.");
        if(!Directory.Exists(packagePath))throw new DirectoryNotFoundException(packagePath);
        foreach(var required in new[]{"DAOrganizer.exe","README.md","LICENSE","CHANGELOG.md","THIRD_PARTY_NOTICES.md","licenses"})
            if(!File.Exists(Path.Combine(packagePath,required))&&!Directory.Exists(Path.Combine(packagePath,required)))
                throw new InvalidDataException($"Portable package is missing required entry: {required}");

        Directory.CreateDirectory(outputPath);
        var channel=match.Groups["beta"].Success?"Beta":"Stable";
        var artifactName=$"DAOrganizer-{version}-win-x64.zip";
        var zipPath=Path.Combine(outputPath,artifactName);
        if(File.Exists(zipPath))File.Delete(zipPath);
        using(var file=File.Create(zipPath))
        using(var archive=new ZipArchive(file,ZipArchiveMode.Create))
        {
            foreach(var source in Directory.EnumerateFiles(packagePath,"*",SearchOption.AllDirectories)
                .OrderBy(x=>Path.GetRelativePath(packagePath,x).Replace('\\','/'),StringComparer.Ordinal))
            {
                var name=Path.GetRelativePath(packagePath,source).Replace('\\','/');
                var entry=archive.CreateEntry(name,CompressionLevel.Optimal);
                entry.LastWriteTime=ArchiveTime;
                entry.ExternalAttributes=0;
                using var input=File.OpenRead(source);using var output=entry.Open();input.CopyTo(output);
            }
        }
        var bytes=File.ReadAllBytes(zipPath);
        var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        File.WriteAllText(zipPath+".sha256",$"{hash}  {artifactName}\n");
        var manifestName=$"release-{channel.ToLowerInvariant()}.json";
        var manifest=new
        {
            schemaVersion=1,channel,version,tag,
            artifact=new{name=artifactName,url=$"https://github.com/{repository}/releases/download/{tag}/{artifactName}",size=bytes.LongLength,sha256=hash},
            releaseNotes=notesUrl,
            compatibility=new{minimumClientVersion="0.15.0",minimumApiVersion="none",minimumProfileSchema=1,maximumProfileSchema=4},
            trust=new{signed=false,informationalOnly=true}
        };
        File.WriteAllText(Path.Combine(outputPath,manifestName),JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true})+"\n");
        Console.WriteLine($"Created {artifactName}, {artifactName}.sha256, and {manifestName}");
    }

    private static void Verify(string tag,string projectPath,string outputPath,string repository)
    {
        var match=TagPattern.Match(tag);
        if(!match.Success)throw new InvalidDataException("Unsupported release tag.");
        var version=tag[1..];
        var project=XDocument.Load(projectPath);
        if(project.Descendants("Version").Select(x=>x.Value.Trim()).SingleOrDefault()!=version)
            throw new InvalidDataException("Tag/version mismatch.");
        var channel=match.Groups["beta"].Success?"Beta":"Stable";
        var name=$"DAOrganizer-{version}-win-x64.zip";
        var path=Path.Combine(outputPath,name);
        var bytes=File.ReadAllBytes(path);
        var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if(File.ReadAllText(path+".sha256").TrimEnd()!=hash+"  "+name)
            throw new InvalidDataException("Release checksum does not match candidate bytes.");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath,$"release-{channel.ToLowerInvariant()}.json")));
        var root=manifest.RootElement;
        var artifact=root.GetProperty("artifact");
        if(root.GetProperty("schemaVersion").GetInt32()!=1||root.GetProperty("channel").GetString()!=channel||
           root.GetProperty("version").GetString()!=version||root.GetProperty("tag").GetString()!=tag||
           artifact.GetProperty("name").GetString()!=name||artifact.GetProperty("size").GetInt64()!=bytes.LongLength||
           artifact.GetProperty("sha256").GetString()!=hash||
           artifact.GetProperty("url").GetString()!=$"https://github.com/{repository}/releases/download/{tag}/{name}"||
           root.GetProperty("trust").GetProperty("signed").GetBoolean()||
           !root.GetProperty("trust").GetProperty("informationalOnly").GetBoolean())
            throw new InvalidDataException("Release manifest does not match candidate, channel, or unsigned policy.");
        using var archive=ZipFile.OpenRead(path);
        var names=archive.Entries.Select(x=>x.FullName).ToArray();
        if(names.Distinct(StringComparer.Ordinal).Count()!=names.Length||
           names.Any(x=>x.StartsWith("/",StringComparison.Ordinal)||x.Contains("../",StringComparison.Ordinal)||x.Contains('\\'))||
           !new[]{"DAOrganizer.exe","README.md","LICENSE","CHANGELOG.md","THIRD_PARTY_NOTICES.md"}.All(names.Contains)||
           !names.Any(x=>x.StartsWith("licenses/",StringComparison.Ordinal)))
            throw new InvalidDataException("Release archive has unsafe or missing entries.");
        Console.WriteLine($"Verified {name} ({channel}, {hash}).");
    }

    private static int SelfTest()
    {
        var root=Path.Combine(Path.GetTempPath(),"daorganizer-release-"+Guid.NewGuid());
        try
        {
            var package=Path.Combine(root,"package");var output=Path.Combine(root,"out");Directory.CreateDirectory(package);
            foreach(var name in new[]{"DAOrganizer.exe","README.md","LICENSE","CHANGELOG.md","THIRD_PARTY_NOTICES.md"})File.WriteAllText(Path.Combine(package,name),name);
            Directory.CreateDirectory(Path.Combine(package,"licenses"));File.WriteAllText(Path.Combine(package,"licenses","PACKAGES.txt"),"licenses");
            File.WriteAllText(Path.Combine(package,"z.txt"),"last");File.WriteAllText(Path.Combine(package,"a.txt"),"first");
            var project=Path.Combine(root,"App.csproj");File.WriteAllText(project,"<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
            Create("v1.2.3",project,package,output,"owner/repo","https://example.test/notes");
            Assert(Run(["verify","--tag","v1.2.3","--project",project,"--output",output,"--repository","owner/repo"])==0,
                "A valid candidate could not be verified before publication.");
            var zip=Path.Combine(output,"DAOrganizer-1.2.3-win-x64.zip");
            var first=File.ReadAllBytes(zip);Create("v1.2.3",project,package,output,"owner/repo","https://example.test/notes");
            Assert(first.SequenceEqual(File.ReadAllBytes(zip)),"ZIP output is not deterministic for identical package bytes.");
            var checksum=File.ReadAllText(zip+".sha256").Split(' ',StringSplitOptions.RemoveEmptyEntries)[0];
            Assert(checksum==Convert.ToHexString(SHA256.HashData(first)).ToLowerInvariant(),"Checksum does not match ZIP bytes.");
            using(var archive=ZipFile.OpenRead(zip))
            {
                var names=archive.Entries.Select(x=>x.FullName).ToArray();
                Assert(names.SequenceEqual(names.OrderBy(x=>x,StringComparer.Ordinal)),"ZIP entries are not ordinally sorted.");
                Assert(names.Contains("DAOrganizer.exe")&&names.Contains("licenses/PACKAGES.txt"),"ZIP content validation failed.");
            }
            using(var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"release-stable.json"))))
            {
                Assert(json.RootElement.GetProperty("channel").GetString()=="Stable","Stable channel was not recorded.");
                Assert(json.RootElement.GetProperty("artifact").GetProperty("size").GetInt64()==first.LongLength,"Manifest size is wrong.");
                Assert(json.RootElement.GetProperty("compatibility").GetProperty("maximumProfileSchema").GetInt32()==4,
                    "Manifest schema compatibility does not match current store.");
                Assert(json.RootElement.GetProperty("trust").GetProperty("signed").GetBoolean()==false,"Unsigned status is wrong.");
            }
            File.WriteAllText(project,"<Project><PropertyGroup><Version>1.2.3-beta.4</Version></PropertyGroup></Project>");
            Create("v1.2.3-beta.4",project,package,output,"owner/repo","https://example.test/notes");
            Verify("v1.2.3-beta.4",project,output,"owner/repo");
            using(var beta=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"release-beta.json"))))
                Assert(beta.RootElement.GetProperty("channel").GetString()=="Beta","Beta channel was not recorded.");
            foreach(var malformed in new[]{"1.2.3","v1.2","v1.2.3-rc.1","v01.2.3","v1.2.3-beta","v1.2.3-beta.01"})
                AssertThrows(()=>Create(malformed,project,package,output,"owner/repo","notes"),$"Malformed tag was accepted: {malformed}");
            AssertThrows(()=>Create("v1.2.3-beta.5",project,package,output,"owner/repo","notes"),"Version mismatch was accepted.");
            File.WriteAllText(project,"<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
            var manifestPath=Path.Combine(output,"release-stable.json");
            var manifestText=File.ReadAllText(manifestPath);
            File.WriteAllText(manifestPath,manifestText.Replace("\"channel\": \"Stable\"","\"channel\": \"Beta\"",StringComparison.Ordinal));
            AssertThrows(()=>Verify("v1.2.3",project,output,"owner/repo"),
                "A mismatched channel manifest was accepted for publication.");
            File.WriteAllText(manifestPath,manifestText);
            File.AppendAllText(zip,"tampered");
            AssertThrows(()=>Verify("v1.2.3",project,output,"owner/repo"),
                "A corrupt candidate was accepted for publication.");
            Console.WriteLine("Release helper self-tests passed.");return 0;
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void AssertThrows(Action action,string message)
    {
        try{action();}
        catch(InvalidDataException){return;}
        throw new InvalidOperationException(message);
    }
}
