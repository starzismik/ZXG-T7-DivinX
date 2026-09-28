using ZXGT7Guard;
using System.Net.Http;
using System.Text.Json;
var current = new Version(1,0,0,0);
var count = 0;
void Check(bool ok) { if (!ok) throw new Exception("Release check failed"); count++; }
string Manifest(string v, string u="https://github.com/starzismik/ZXG-T7-DivinX/releases/tag/v1.0.0") => JsonSerializer.Serialize(new { version=v, downloadUrl=u });
Check(UpdateManifest.Parse(Manifest("1.0.0")).Version == current);
Check(UpdateManifest.Parse(Manifest("1.0.1")).Version > current);
Check(UpdateManifest.Parse(Manifest("0.9.0")).Version < current);
foreach (var bad in new[]{"{}", "not json", Manifest("1.0"), Manifest("oops"), Manifest("1.0.0","http://example.com"), Manifest("1.0.0","https://user:pass@example.com")}) {
 bool rejected=false; try { UpdateManifest.Parse(bad); } catch { rejected=true; } Check(rejected);
}
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(15) };
using var response = await client.GetAsync("https://raw.githubusercontent.com/starzismik/ZXG-T7-DivinX/main/update.json");
Check(response.IsSuccessStatusCode);
var live=UpdateManifest.Parse(await response.Content.ReadAsStringAsync());
Check(live.Version == current);
Console.WriteLine($"PASS: {count} release checks; public manifest reachable without redirects.");
