using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Updates
{
    internal static readonly Version Current = Assembly.GetExecutingAssembly().GetName().Version;
    private const string Repo = "LiveTrack-X/BatteryWattChecker";
    private const string Filename = "BatteryWattChecker.exe";
    private static readonly string DirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly string PendingPath = Path.Combine(DirectoryPath, "BatteryWattChecker.pending.exe");
    private static readonly string InstalledPath = Path.Combine(DirectoryPath, Filename);
    private static readonly HttpClient Client = CreateClient();

    internal sealed class Release {
        public string tag_name { get; set; }
        public Asset[] assets { get; set; }
        public Version Version { get { return new Version(tag_name.Substring(1)); } }
    }
    internal sealed class Asset {
        public string name { get; set; }
        public string digest { get; set; }
    }

    private static HttpClient CreateClient()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 16 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BatteryWattChecker/" + Current.ToString(3));
        return client;
    }

    internal static async Task<Release> Latest()
    {
        string json = await Client.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest");
        var release = new JavaScriptSerializer().Deserialize<Release>(json);
        Version version;
        if (release == null || String.IsNullOrEmpty(release.tag_name) || !release.tag_name.StartsWith("v") ||
            !Version.TryParse(release.tag_name.Substring(1), out version) || version.Build < 0)
            throw new InvalidDataException("최신 릴리스의 버전을 확인할 수 없습니다.");
        return release;
    }

    internal static async Task Stage(Release release)
    {
        if (!String.Equals(Application.ExecutablePath, InstalledPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("파일 이름을 BatteryWattChecker.exe로 복원한 뒤 다시 실행해 주세요.");
        var asset = Array.Find(release.assets ?? new Asset[0], a => a.name == Filename);
        if (asset == null || String.IsNullOrEmpty(asset.digest))
            throw new InvalidDataException("릴리스에 실행 파일 또는 SHA-256 검증값이 없습니다.");
        var bytes = await Client.GetByteArrayAsync("https://github.com/" + Repo + "/releases/download/" + release.tag_name + "/" + Filename);
        using (var sha = SHA256.Create()) {
            string digest = "sha256:" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            if (!String.Equals(digest, asset.digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("다운로드한 파일의 SHA-256이 릴리스와 일치하지 않습니다.");
        }
        string temp = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        try {
            File.WriteAllBytes(temp, bytes);
            var version = AssemblyName.GetAssemblyName(temp).Version;
            if (version.ToString(3) != release.Version.ToString(3))
                throw new InvalidDataException("실행 파일과 릴리스의 버전이 다릅니다.");
            if (File.Exists(PendingPath)) File.Delete(PendingPath);
            File.Move(temp, PendingPath);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void WaitForExit(string id)
    {
        int pid;
        if (!Int32.TryParse(id, out pid) || pid <= 0 || pid == Process.GetCurrentProcess().Id)
            throw new InvalidDataException("업데이트 프로세스 정보를 확인할 수 없습니다.");
        Process previous;
        try { previous = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (previous) {
            if (!previous.WaitForExit(20000)) throw new IOException("이전 프로세스가 아직 실행 중입니다.");
        }
    }

    internal static void Install(string previousId)
    {
        if (!String.Equals(Application.ExecutablePath, PendingPath, StringComparison.OrdinalIgnoreCase)) return;
        string temp = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        string backup = temp + ".bak";
        try {
            WaitForExit(previousId);
            File.Copy(PendingPath, temp);
            File.Replace(temp, InstalledPath, backup);
            try { Process.Start(InstalledPath, "--updated " + Process.GetCurrentProcess().Id); }
            catch {
                File.Replace(backup, InstalledPath, null);
                Process.Start(InstalledPath);
                throw;
            }
            File.Delete(backup);
        } catch (Exception ex) { MessageBox.Show(ex.Message, "업데이트 설치 오류"); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static void Cleanup(string previousId)
    {
        try { WaitForExit(previousId); File.Delete(PendingPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
