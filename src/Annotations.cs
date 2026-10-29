using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;

namespace Afterimage
{
    internal sealed class AnnotationTrack
    {
        internal sealed class Cue { public double at_ms; public double source_at_ms; public double end_ms; public string text; }
        private readonly List<Cue> cues = new List<Cue>();
        private double start;
        private string app = "";
        internal void Begin(double elapsed) { cues.Clear(); start = elapsed; if (app.Length > 0) cues.Add(new Cue { at_ms = 0, text = "App: " + app }); }
        internal void Add(Dictionary<string, object> e)
        {
            string kind = (string)e["type"]; double at = Convert.ToDouble(e["elapsed_ms"], CultureInfo.InvariantCulture) - start;
            string text = null;
            if (kind == "foreground_app") { app = Convert.ToString(e["process"], CultureInfo.InvariantCulture); text = "App: " + app; if (e.ContainsKey("window_title")) text += " — " + e["window_title"]; }
            else if (kind.StartsWith("mouse_")) {
                string action = kind == "mouse_left_click" ? "Left click" : kind == "mouse_right_click" ? "Right click" : kind == "mouse_middle_click" ? "Middle click" : kind == "mouse_scroll" ? (Convert.ToInt32(e["wheel_delta"]) > 0 ? "Scroll up" : "Scroll down") : "Mouse action";
                text = action + " at (" + e["screen_x"] + ", " + e["screen_y"] + ")" + (app.Length > 0 ? " · " + app : "");
            } else if (kind == "workflow_marker") text = (e.TryGetValue("label", out var label) ? Convert.ToString(label, CultureInfo.InvariantCulture) : "Workflow step marked") + (app.Length > 0 ? " · " + app : "");
            // Bound annotation memory even during very heavy input. Full events remain on disk.
            // Encoder startup can leave a small gap before the first frame of a new clip.
            // Keep that activity at the nearest playable instant; retain its original offset too.
            if (text != null && cues.Count < 20000) cues.Add(new Cue { at_ms = Math.Max(0, at), source_at_ms = at, text = text.Replace('\r', ' ').Replace('\n', ' ') });
        }
        internal void Save(string folder, string video, double duration)
        {
            Cue[] ordered = cues.OrderBy(c => c.at_ms).Select(c => new Cue { at_ms = Math.Min(c.at_ms, Math.Max(0, duration - 1)), source_at_ms = c.source_at_ms, text = c.text }).ToArray();
            StringBuilder srt = new StringBuilder();
            for (int i = 0; i < ordered.Length; i++) {
                Cue c = ordered[i]; c.end_ms = Math.Min(duration, Math.Min(c.at_ms + 2500, i + 1 < ordered.Length ? ordered[i + 1].at_ms : duration));
                if (c.end_ms <= c.at_ms) continue;
                srt.AppendLine((i + 1).ToString(CultureInfo.InvariantCulture)); srt.AppendLine(Time(c.at_ms) + " --> " + Time(c.end_ms)); srt.AppendLine(c.text); srt.AppendLine();
            }
            string stem = Path.GetFileNameWithoutExtension(video);
            File.WriteAllText(Path.Combine(folder, stem + ".srt"), srt.ToString(), new UTF8Encoding(false));
            Json.Save(Path.Combine(folder, stem + ".annotations.json"), new { video = video, duration_ms = duration, annotations = ordered });
            string html = @"<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Workflow review</title>
<style>*{box-sizing:border-box}body{margin:0;background:#f6f7fb;color:#202539;font:15px system-ui,sans-serif}header{padding:20px 26px;border-bottom:1px solid #dedfeb;background:white}h1{font-size:22px;margin:0 0 5px}small{color:#697185}main{display:grid;grid-template-columns:minmax(0,1fr) 310px;gap:22px;padding:24px}.screen{position:relative;background:#111;border-radius:10px;overflow:hidden}video{display:block;width:100%;max-height:75vh}.caption{position:absolute;left:3%;right:3%;bottom:60px;text-align:center;pointer-events:none}.caption span{background:#141827e8;color:white;padding:8px 13px;border-radius:6px;box-decoration-break:clone;line-height:1.9}aside{background:white;border:1px solid #e2e4ee;border-radius:10px;padding:18px;max-height:82vh;overflow:auto}h2{font-size:16px;margin:0 0 14px}button{display:block;background:white;color:#333a51;border:0;border-left:3px solid #e6e7f0;width:100%;text-align:left;padding:10px 12px;cursor:pointer}button:hover,button.active{background:#eeecff;border-color:#6551d6}time{display:block;font-size:11px;color:#7b7399;margin-bottom:4px}p{color:#697185;font-size:13px;line-height:1.6}@media(max-width:800px){main{grid-template-columns:1fr}aside{max-height:250px}}</style>
<header><h1>Workflow review</h1><small id='name'></small></header><main><section><div class='screen'><video id='video' controls preload='metadata'></video><div class='caption'><span id='caption' hidden></span></div></div><p>Click an event to jump to it. Labels describe observed app and mouse activity. The original video is unmodified.</p></section><aside><h2>Activity timeline</h2><div id='events'></div></aside></main>
<script>const data=__DATA__;const v=document.getElementById('video');v.src=data.video;document.getElementById('name').textContent=data.video;const cap=document.getElementById('caption');const root=document.getElementById('events');const buttons=[];let active=-1;for(const [i,c] of data.annotations.entries()){const b=document.createElement('button');const t=document.createElement('time');t.textContent=(c.at_ms/1000).toFixed(1)+'s';const label=document.createElement('span');label.textContent=c.text;b.append(t,label);b.onclick=()=>{v.currentTime=c.at_ms/1000;v.play().catch(()=>{});};root.append(b);buttons.push(b);}if(!buttons.length)root.textContent='No automatic annotations in this clip.';v.ontimeupdate=()=>{const ms=v.currentTime*1000;let lo=0,hi=data.annotations.length-1,found=-1;while(lo<=hi){const m=(lo+hi)>>1;if(data.annotations[m].at_ms<=ms){found=m;lo=m+1;}else hi=m-1;}const c=data.annotations[found];cap.hidden=!(c&&ms<c.end_ms);cap.textContent=c?c.text:'';if(found!==active){if(active>=0)buttons[active].classList.remove('active');if(found>=0)buttons[found].classList.add('active');active=found;}};</script></html>";
            // Escape '<' so captured window titles cannot terminate the script tag.
            string data = Json.Encode(new { video = video, annotations = ordered }).Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
            File.WriteAllText(Path.Combine(folder, stem + ".review.html"), html.Replace("__DATA__", data), new UTF8Encoding(false));
            SaveIndex(folder);
        }
        private static string Time(double ms) { TimeSpan t = TimeSpan.FromMilliseconds(Math.Max(0, ms)); return ((int)t.TotalHours).ToString("D2") + ":" + t.Minutes.ToString("D2") + ":" + t.Seconds.ToString("D2") + "," + t.Milliseconds.ToString("D3"); }
        private static void SaveIndex(string folder)
        {
            string[] files = Directory.GetFiles(folder, "clip-*.review.html").OrderBy(x => x, StringComparer.Ordinal).ToArray();
            StringBuilder links = new StringBuilder();
            foreach (string path in files) { string name = Path.GetFileName(path); links.Append("<a target='review' href='").Append(WebUtility.HtmlEncode(name)).Append("'>").Append(WebUtility.HtmlEncode(name.Replace(".review.html", ""))).Append("</a>"); }
            string html = "<!doctype html><html lang='en'><meta charset='utf-8'><title>Afterimage — session</title><style>body{margin:0;font:14px system-ui;background:#f6f7fb}nav{display:flex;gap:8px;align-items:center;padding:12px;overflow-x:auto;border-bottom:1px solid #ddd}nav strong{white-space:nowrap;margin-right:14px}a{color:#4c3fd0;background:white;border:1px solid #ddd;border-radius:5px;padding:7px;text-decoration:none;white-space:nowrap}iframe{width:100%;height:calc(100vh - 65px);border:0}</style><nav><strong>Session clips</strong>" + links + "</nav><iframe title='Clip review' name='review' src='" + (files.Length > 0 ? Path.GetFileName(files[0]) : "about:blank") + "'></iframe></html>";
            string pathOut = Path.Combine(folder, "Review.html"), temp = pathOut + ".tmp"; File.WriteAllText(temp, html, new UTF8Encoding(false)); if (File.Exists(pathOut)) File.Replace(temp, pathOut, null); else File.Move(temp, pathOut);
        }
    }
}
