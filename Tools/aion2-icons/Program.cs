using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Options;

var dir = args[0];
var key = Environment.GetEnvironmentVariable("AION_KEY");
var provider = new DefaultFileProvider(dir, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_Aion2));
provider.MappingsContainer = new EmptyMappings();
provider.Initialize();
provider.SubmitKey(new FGuid(), new FAesKey(key));
Console.WriteLine($"files: {provider.Files.Count}");
if (args[1] == "list")
{
    File.WriteAllLines(args[2], provider.Files.Keys.OrderBy(k => k));
}
else if (args[1] == "dump")
{
    // dump <outdir> <substring>...: raw cooked packages (uasset) of every match
    Directory.CreateDirectory(args[2]);
    int n = 0;
    foreach (var path in provider.Files.Keys.Where(k => k.EndsWith(".uasset") && args.Skip(3).Any(f => k.Contains(f, StringComparison.OrdinalIgnoreCase))))
    {
        if (provider.TrySavePackage(path, out var parts)) { foreach (var kv in parts) File.WriteAllBytes(Path.Combine(args[2], Path.GetFileName(kv.Key)), kv.Value); n++; }
    }
    Console.WriteLine($"dumped {n}");
}
else if (args[1] == "table")
{
    // table <outdir> <name>...: decrypted DataTable .dat files
    Directory.CreateDirectory(args[2]);
    CUE4Parse.GameTypes.Aion2.Encryption.Aes.Aion2DatFileEncryption.Initialize(provider);
    foreach (var name in (args[3] == "*" ? provider.Files.Keys.Where(k => k.Contains("Data/Table/") && k.EndsWith(".dat")).Select(k => Path.GetFileNameWithoutExtension(k)).Distinct().ToArray() : args.Skip(3).ToArray()))
    {
        var gf = provider.Files.First(k => k.Key.EndsWith("Data/Table/" + name + ".dat", StringComparison.OrdinalIgnoreCase)).Value; try {
        var data = gf.SafeRead();
        if (data.Length >= 8 && BitConverter.ToUInt32(data, 0) == 13) data = CUE4Parse.GameTypes.Aion2.Encryption.Aes.Aion2DatFileEncryption.DecryptDataTable(data, gf.Path);
        File.WriteAllBytes(Path.Combine(args[2], name + ".bin"), data);
        } catch (Exception e) { Console.WriteLine(name + " ERR " + e.Message); }
    }
}
else if (args[1] == "raw")
{
    var path = provider.Files.Keys.First(k => k.Contains(args[2], StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset"));
    provider.TrySavePackage(path, out var parts);
    foreach (var kv in parts) { Console.WriteLine($"{kv.Key} {kv.Value.Length}"); File.WriteAllBytes(Path.Combine(args[3], Path.GetFileName(kv.Key)), kv.Value); }
}
else if (args[1] == "export")
{
    // export <outdir> <substring> [max]
    var max = args.Length > 4 ? int.Parse(args[4]) : int.MaxValue;
    int n = 0, fail = 0;
    foreach (var path in provider.Files.Keys.Where(k => k.Contains(args[3], StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset")).Take(max))
    {
        try
        {
            var tex = provider.LoadPackageObject<UTexture2D>(path.Replace(".uasset", "") + "." + Path.GetFileNameWithoutExtension(path));
            var bmp = tex.Decode();
            if (bmp == null) { fail++; if (fail<3) Console.WriteLine($"{path}: format={tex.Format} pd={(tex.PlatformData==null?"null":tex.PlatformData.SizeX+"x"+tex.PlatformData.SizeY+" mips="+tex.PlatformData.Mips.Length+" fmt="+tex.PlatformData.PixelFormat)} first={(tex.GetFirstMip()==null?"null":"ok")} bulk={(tex.GetFirstMip()?.BulkData?.Data==null?"nodata":"data")}"); continue; }
            var o = Path.Combine(args[2], Path.GetFileNameWithoutExtension(path) + ".png");
            Directory.CreateDirectory(args[2]);
            var bytes = bmp.Encode(ETextureFormat.Png, false, out _);
            File.WriteAllBytes(o, bytes);
            n++;
        }
        catch (Exception e) { fail++; if (fail < 5) Console.WriteLine(path + ": " + e.Message); }
    }
    Console.WriteLine($"exported {n}, failed {fail}");
}

class EmptyMappings : CUE4Parse.MappingsProvider.ITypeMappingsProvider
{
    public CUE4Parse.MappingsProvider.TypeMappings MappingsForGame { get; } = new();
    public void Load(string path, StringComparer c = null) { }
    public void Load(byte[] b, StringComparer c = null) { }
    public void Reload() { }
}
