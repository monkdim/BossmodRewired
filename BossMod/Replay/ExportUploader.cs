using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BossMod;

/// <summary>
/// Sends a finished fight somewhere, if and only if somebody has asked for that.
///
/// Two things have to be true before anything leaves: somebody answered yes during setup, and the build has
/// somewhere to send to. The answer has no default and is asked outright, so a fresh install sends nothing
/// while the question is still unanswered. The address may be shipped, because a build that asks the question
/// and then has nowhere to put the answer is asking for nothing.
///
/// The address is a relay, not a final destination. Posting an export straight at storage would mean shipping
/// that storage's credential inside the plugin, where anybody who installs it can read it back out. The relay
/// holds the credential instead and decides what to do with what it receives. That also means a chat webhook
/// is not a valid address here: those want their own message shape, not a fight export, and would refuse
/// every submission.
///
/// Failures are quiet in the game and loud in the log. An upload that did not happen costs nothing, since the
/// export it was made from is still sitting on disk and can be sent again; an error box during a pull costs a
/// pull.
///
/// It sends whatever gets exported, when it gets exported, which today means once per duty. Sending once per
/// boss instead would need something that does not exist: the analysis reads a finished log file, and while a
/// duty is running the recorder is still writing one. Nothing keeps a parsed replay in memory alongside it, so
/// a per-boss send means either flushing and re-reading a partial log at every boss or maintaining a second
/// live copy of the whole replay. That is a piece of work rather than a setting, and a switch promising it
/// before it exists would be worse than not offering one.
/// </summary>
public static class ExportUploader
{
    // Kept for the plugin's lifetime rather than per send. Creating one per upload is the classic way to
    // exhaust sockets, and a raid night is a lot of uploads.
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // Sends run one after another rather than all at once. A duty finishing produces a single export and would
    // not care either way, but exporting a folder of recordings produces a hundred within a few seconds, and a
    // hundred at once is both a burst nobody asked the relay to absorb and a pile of writes racing each other
    // for the same branch. A queue costs nothing here: nothing is waiting on the result.
    private static readonly object _gate = new();
    private static Task _chain = Task.CompletedTask;

    /// <summary>
    /// The relay a build ships pointed at, used when nobody has set one by hand.
    ///
    /// Safe to have here, and safe to hand out. It is a write-only front door: it accepts exports, holds no
    /// credential of its own, and cannot be read back through. The token that lets it file anything lives in
    /// the relay's own secrets, which is the entire reason the relay exists.
    ///
    /// Empty means a build sends nowhere no matter what anybody answers during setup, which is the correct
    /// state for a fork that has not deployed a relay of its own. Anybody forking this should replace it or
    /// clear it rather than quietly posting somebody else's fights at somebody else's storage.
    /// </summary>
    public const string DefaultEndpoint = "https://bossmod-rewired-relay.dimaggio-colby.workers.dev";

    private static SetupConfig Config => Service.Config.Get<SetupConfig>();

    /// <summary>Where this install would actually post, whether that was chosen or shipped.</summary>
    public static string Endpoint
    {
        get
        {
            var chosen = Config.ShareEndpoint.Trim();
            return chosen.Length > 0 ? chosen : DefaultEndpoint;
        }
    }

    /// <summary>Whether anything would be sent at all, so the caller can skip the work of preparing it.</summary>
    public static bool Wanted => Config.ShareFightData && Endpoint.Length > 0;

    /// <summary>
    /// Sends one export, on a thread that is not the game's.
    ///
    /// Takes the body rather than a path, because what is sent is not always what was written: a recording
    /// that captured damage keeps it on disk and sends a copy without it, unless that was asked for too.
    ///
    /// Fire and forget on purpose. The caller has already written the file, which is the part that matters;
    /// whether it also reached a server is not something worth making anybody wait for. It joins a queue
    /// rather than starting immediately, so exporting a folder of recordings arrives as a hundred sends in a
    /// row instead of a hundred at once.
    /// </summary>
    public static void Send(string name, string json)
    {
        if (!Wanted)
        {
            return;
        }

        var endpoint = Submit(Endpoint);
        lock (_gate)
        {
            _chain = _chain.ContinueWith(_ => SendOne(name, json, endpoint), TaskScheduler.Default).Unwrap();
        }
    }

    private static async Task SendOne(string path, string json, string endpoint)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync(endpoint, content);
            var reply = await res.Content.ReadAsStringAsync();

            if (res.IsSuccessStatusCode)
            {
                Service.Log($"[upload] sent {Path.GetFileName(path)}: {reply}");
            }
            else
            {
                Service.Log($"[upload] {Path.GetFileName(path)} refused with {(int)res.StatusCode}: {reply}");
            }
        }
        catch (Exception e)
        {
            // Never surfaced in game. The file is still on disk, so nothing has been lost that cannot be
            // sent again, and a raid is the worst possible moment to be told about a network error.
            Service.Log($"[upload] could not send {Path.GetFileName(path)}: {e.Message}");
        }
    }

    /// <summary>
    /// The address to post to.
    ///
    /// A relay accepts submissions on one path, and somebody pasting its address is more likely to paste the
    /// root than to remember that. Adding the path when it is missing costs nothing; adding it twice would
    /// produce a silent stream of rejections, so an address that already carries it is left alone.
    /// </summary>
    private static string Submit(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        return trimmed.EndsWith("/submit", StringComparison.OrdinalIgnoreCase) ? trimmed : $"{trimmed}/submit";
    }
}
