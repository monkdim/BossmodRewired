using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BossMod;

/// <summary>
/// A box to say what went wrong, without leaving the game.
///
/// The reason this exists rather than a link to the issue tracker is that the link is where reports go to
/// not happen. Somebody notices the radar vanished in the middle of a pull, and by the time they are at a
/// keyboard with a browser open they no longer remember which pull, which fight, or what version they were
/// on. The three facts that make a report worth reading are the three nobody writes down, and all three are
/// sitting right here while it is happening.
///
/// It posts at the same relay the exports go to, for the same reason: opening an issue needs a credential,
/// and a credential shipped inside a plugin is a credential anybody who installs it can read back out.
///
/// Nothing here is automatic. Feedback is sent when somebody writes something and presses the button, and
/// the box says plainly what goes with it.
/// </summary>
public sealed class FeedbackTab(WorldState ws, BossModuleManager bossmod)
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private string _message = "";
    private string _contact = "";
    private string _status = "";
    private volatile bool _sending;

    // Cleared on the next frame rather than the moment the reply arrives. The text box holds a reference to
    // the message while it is being drawn, and the reply comes back on a thread that is not the one drawing,
    // so emptying it from there means changing what ImGui is in the middle of reading.
    private volatile bool _clearOnNextDraw;

    /// <summary>What the plugin says it is, taken from the assembly rather than from a constant somebody
    /// has to remember to bump.</summary>
    private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public void Draw()
    {
        if (_clearOnNextDraw)
        {
            _message = "";
            _clearOnNextDraw = false;
        }

        using var wrap = ImRaii.TextWrapPos(0);

        ImGui.TextUnformatted("Something not working, or working oddly? Say so here and it becomes an issue on the");
        ImGui.TextUnformatted("repository. Most useful while it is still happening, since what you were doing goes with it.");
        ImGui.Spacing();

        ImGui.TextUnformatted("What happened");
        ImGui.InputTextMultiline("##feedback", ref _message, 4000, new(-1f, ImGui.GetTextLineHeight() * 8f));

        ImGui.Spacing();
        ImGui.SetNextItemWidth(300);
        ImGui.InputTextEx("##contact", "Discord handle, if you want an answer (optional)", ref _contact);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Shown rather than described, because "some diagnostic information" is the sentence that makes
        // people stop trusting a feedback box. This is the whole of it.
        ImGui.TextDisabled("Sent with it:");
        ImGui.TextDisabled($"    plugin {Version}");
        ImGui.TextDisabled($"    zone {(ws.CurrentZone != 0 ? ws.CurrentZone.ToString() : "not in a duty")}");
        ImGui.TextDisabled($"    module {ModuleName()}");
        ImGui.TextDisabled("Nothing else. Not your name, not your log, not your recordings.");

        ImGui.Spacing();

        var relay = ExportUploader.Endpoint;
        var ready = !_sending && _message.Trim().Length >= 10 && relay.Length > 0;

        using (ImRaii.Disabled(!ready))
        {
            if (ImGui.Button(_sending ? "Sending..." : "Send", new(120, 0)))
            {
                Send(relay);
            }
        }

        if (relay.Length == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("This build has no relay set, so there is nowhere to send it.");
        }
        else if (_message.Trim().Length < 10)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("A sentence or two is enough.");
        }

        if (_status.Length > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted(_status);
        }
    }

    /// <summary>The fight currently on screen, if any. A report about the radar is almost always a report
    /// about whichever module was drawing it at the time, and that is the one fact the reporter is least
    /// likely to know they should mention.</summary>
    private string ModuleName() => bossmod.ActiveModule?.GetType().Name ?? "none loaded";

    private void Send(string relay)
    {
        _sending = true;
        _status = "";

        var body = Payload();
        var endpoint = $"{relay.TrimEnd('/')}/feedback";

        // Off the game's thread, like everything else that touches the network. The result comes back as a
        // line of text under the button rather than anywhere that could interrupt a pull.
        _ = Task.Run(async () =>
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var res = await _http.PostAsync(endpoint, content);
                var reply = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode)
                {
                    _status = $"Sent, {reply}. Thank you.";
                    _clearOnNextDraw = true;
                }
                else
                {
                    _status = $"Not sent: {reply}";
                }

                Service.Log($"[feedback] {(int)res.StatusCode}: {reply}");
            }
            catch (Exception e)
            {
                _status = $"Could not send it: {e.Message}";
                Service.Log($"[feedback] failed: {e.Message}");
            }
            finally
            {
                _sending = false;
            }
        });
    }

    /// <summary>
    /// Hand-rolled, and invariant, for the same reason the exports are: a serializer that respects the
    /// machine's locale will happily put a comma where this needs a full stop, and the relay is not going
    /// to guess what was meant.
    /// </summary>
    private string Payload()
    {
        var sb = new StringBuilder();
        sb.Append("{\"message\":").Append(Quote(_message.Trim()));
        sb.Append(",\"contact\":").Append(Quote(_contact.Trim()));
        sb.Append(",\"version\":").Append(Quote(Version));
        sb.Append(",\"zone\":").Append(ws.CurrentZone.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"module\":").Append(Quote(ModuleName()));
        sb.Append('}');
        return sb.ToString();
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"", s.Length + 16);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
