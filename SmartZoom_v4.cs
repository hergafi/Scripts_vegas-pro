using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// ============================================================
//  Vegasaur Toolkit for VEGAS Pro 2026 — Smart Zoom v4
//  Rebuild of Vegasaur Smart Zoom / Motion tools using
//  ScriptPortal.Vegas API (compatible with VEGAS Pro 2026).
//  Written in C# 2.0-compatible syntax for VEGAS Script Portal.
// ============================================================

public class EntryPoint
{
    private static SmartZoomWindow activePanel;

    public void FromVegas(Vegas vegas)
    {
        if (activePanel != null)
        {
            activePanel.Focus();
            return;
        }

        List<VideoEvent> all = GetVideoEvents(vegas);
        if (all.Count == 0)
        {
            MessageBox.Show("No video events found in the timeline.", "Vegasaur Toolkit — Smart Zoom",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        List<EventSnapshot> snapshots = CaptureSnapshots(all);
        activePanel = new SmartZoomWindow(vegas, all, snapshots);
        activePanel.FormClosed += new FormClosedEventHandler(Panel_FormClosed);
        activePanel.ShowDialog(vegas.MainWindow);
        activePanel = null;
    }

    private static void Panel_FormClosed(object sender, FormClosedEventArgs e)
    {
        activePanel = null;
    }

    private static List<VideoEvent> GetVideoEvents(Vegas vegas)
    {
        List<VideoEvent> list = new List<VideoEvent>();
        foreach (Track track in vegas.Project.Tracks)
        {
            if (!track.IsVideo()) continue;
            foreach (TrackEvent te in track.Events)
            {
                VideoEvent ve = te as VideoEvent;
                if (ve != null) list.Add(ve);
            }
        }
        return list;
    }

    private static List<VideoEvent> GetTargets(List<VideoEvent> all, int applyTo)
    {
        List<VideoEvent> selected = new List<VideoEvent>();
        foreach (VideoEvent v in all)
            if (v.Selected) selected.Add(v);
        if (applyTo == 1) return selected;
        if (applyTo == 2) return all;
        return selected.Count > 0 ? selected : all;
    }

    // ----------------------------------------------------------
    //  Motion engine (rebuild of Vegasaur SmartZoom motion logic)
    // ----------------------------------------------------------
    private static bool ApplyMotion(Vegas vegas, VideoEvent ev, MotionSettings s)
    {
        VideoMotion motion = ev.VideoMotion;
        if (motion == null || motion.Keyframes == null || motion.Keyframes.Count == 0) return false;

        VideoMotionKeyframe original = motion.Keyframes[0];
        VideoMotionBounds baseBounds = CloneBounds(original.Bounds);
        VideoMotionVertex baseCenter = CloneVertex(original.Center);
        double baseRotation = original.Rotation;
        Timecode length = ev.Length;
        double w = vegas.Project.Video.Width;
        double h = vegas.Project.Video.Height;

        VideoMotionKeyframes keys = motion.Keyframes;
        keys.Clear();
        VideoMotionKeyframe first = keys[0];

        double zoom = 1.0 + s.ZoomPercent / 100.0;
        double zoomed = s.KeepFullFrame ? (1.0 / zoom) : zoom;
        double startScale = s.Reverse ? zoomed : 1.0;
        double endScale = s.Reverse ? 1.0 : zoomed;

        double panX = s.PanXPercent / 100.0 * w;
        double panY = s.PanYPercent / 100.0 * h;
        double startPanX = s.Reverse ? panX : 0.0;
        double startPanY = s.Reverse ? panY : 0.0;
        double endPanX = s.Reverse ? 0.0 : panX;
        double endPanY = s.Reverse ? 0.0 : panY;

        double rot = s.RotationDeg * Math.PI / 180.0;
        double startRot = s.Reverse ? baseRotation + rot : baseRotation;
        double endRot = s.Reverse ? baseRotation : baseRotation + rot;

        if (s.BeatSync)
        {
            ApplyBeatPulse(vegas, keys, baseBounds, baseCenter, baseRotation, length, zoomed, s, w, h);
            return true;
        }

        first.Position = Timecode.FromNanos(0);
        first.Bounds = TransformBounds(baseBounds, baseCenter, startScale, startPanX, startPanY);
        first.Center = CloneVertex(baseCenter);
        first.Rotation = startRot;

        int points = Clamp(s.Points, 0, 40);
        for (int i = 1; i <= points; i++)
        {
            double t = (double)i / (points + 1);
            double eased = MotionSettings.Ease(t, s.Easing);

            double scale = MotionSettings.Lerp(startScale, endScale, eased);
            double x = MotionSettings.Lerp(startPanX, endPanX, eased);
            double y = MotionSettings.Lerp(startPanY, endPanY, eased);
            double r = MotionSettings.Lerp(startRot, endRot, eased);

            Timecode pos = new Timecode(length.ToMilliseconds() * t);
            VideoMotionKeyframe key = new VideoMotionKeyframe(vegas.Project, pos);
            keys.Add(key);
            key.Bounds = TransformBounds(baseBounds, baseCenter, scale, x, y);
            key.Center = CloneVertex(baseCenter);
            key.Rotation = r;
        }

        VideoMotionKeyframe endKey = new VideoMotionKeyframe(vegas.Project, length);
        keys.Add(endKey);
        endKey.Bounds = TransformBounds(baseBounds, baseCenter, endScale, endPanX, endPanY);
        endKey.Center = CloneVertex(baseCenter);
        endKey.Rotation = endRot;

        return true;
    }

    private static void ApplyBeatPulse(Vegas vegas, VideoMotionKeyframes keys,
        VideoMotionBounds baseBounds, VideoMotionVertex baseCenter, double baseRotation,
        Timecode length, double zoomed, MotionSettings s, double w, double h)
    {
        int beatFrames = Math.Max(2, s.BeatFrames);
        double panX = s.PanXPercent / 100.0 * w;
        double panY = s.PanYPercent / 100.0 * h;
        double rot = s.RotationDeg * Math.PI / 180.0;

        List<long> positions = new List<long>();
        long current = 0;
        while (current < length.FrameCount)
        {
            positions.Add(current);
            current += beatFrames;
        }
        positions.Add(length.FrameCount);

        if (positions.Count > 600)
        {
            long step = (long)Math.Ceiling((double)length.FrameCount / 600.0);
            positions.Clear();
            current = 0;
            while (current < length.FrameCount)
            {
                positions.Add(current);
                current += step;
            }
            positions.Add(length.FrameCount);
        }

        for (int i = 0; i < positions.Count; i++)
        {
            long frame = positions[i];
            double t = length.FrameCount == 0 ? 0.0 : (double)frame / (double)length.FrameCount;
            double eased = MotionSettings.Ease(t, s.Easing);

            double pancurX = MotionSettings.Lerp(0.0, panX, eased);
            double pancurY = MotionSettings.Lerp(0.0, panY, eased);
            double rotation = baseRotation + eased * rot;

            double scale = (i % 2 == 0) ? 1.0 : zoomed;
            if (frame == length.FrameCount) scale = 1.0;

            Timecode pos = Timecode.FromFrames(frame);
            VideoMotionKeyframe key;
            if (i == 0)
            {
                key = keys[0];
                key.Position = pos;
            }
            else
            {
                key = new VideoMotionKeyframe(vegas.Project, pos);
                keys.Add(key);
            }
            key.Bounds = TransformBounds(baseBounds, baseCenter, scale, pancurX, pancurY);
            key.Center = CloneVertex(baseCenter);
            key.Rotation = rotation;
        }
    }

    private static VideoMotionBounds TransformBounds(VideoMotionBounds b, VideoMotionVertex c, double scale, double px, double py)
    {
        double cx = c.X, cy = c.Y;
        return new VideoMotionBounds(
            new VideoMotionVertex((float)((b.TopLeft.X - cx) * scale + cx + px), (float)((b.TopLeft.Y - cy) * scale + cy + py)),
            new VideoMotionVertex((float)((b.TopRight.X - cx) * scale + cx + px), (float)((b.TopRight.Y - cy) * scale + cy + py)),
            new VideoMotionVertex((float)((b.BottomRight.X - cx) * scale + cx + px), (float)((b.BottomRight.Y - cy) * scale + cy + py)),
            new VideoMotionVertex((float)((b.BottomLeft.X - cx) * scale + cx + px), (float)((b.BottomLeft.Y - cy) * scale + cy + py)));
    }

    // ----------------------------------------------------------
    //  Snapshots (capture/restore original motion)
    // ----------------------------------------------------------
    private static List<EventSnapshot> CaptureSnapshots(List<VideoEvent> events)
    {
        List<EventSnapshot> snaps = new List<EventSnapshot>();
        foreach (VideoEvent ev in events)
        {
            EventSnapshot snap = new EventSnapshot();
            snap.Event = ev;
            VideoMotion motion = ev.VideoMotion;
            if (motion != null && motion.Keyframes != null)
            {
                foreach (VideoMotionKeyframe k in motion.Keyframes)
                {
                    snap.Keyframes.Add(new KeyframeSnapshot());
                    KeyframeSnapshot ks = snap.Keyframes[snap.Keyframes.Count - 1];
                    ks.PositionNanos = k.Position.Nanos;
                    ks.Bounds = CloneBounds(k.Bounds);
                    ks.Center = CloneVertex(k.Center);
                    ks.Rotation = k.Rotation;
                }
            }
            snaps.Add(snap);
        }
        return snaps;
    }

    private static void RestoreSnapshots(Vegas vegas, List<EventSnapshot> snapshots)
    {
        foreach (EventSnapshot snap in snapshots)
        {
            VideoMotion motion = snap.Event.VideoMotion;
            if (motion == null || motion.Keyframes == null || snap.Keyframes.Count == 0) continue;
            VideoMotionKeyframes keys = motion.Keyframes;
            keys.Clear();
            for (int i = 0; i < snap.Keyframes.Count; i++)
            {
                KeyframeSnapshot ks = snap.Keyframes[i];
                if (i == 0)
                {
                    VideoMotionKeyframe first = keys[0];
                    first.Position = Timecode.FromNanos(ks.PositionNanos);
                    first.Bounds = CloneBounds(ks.Bounds);
                    first.Center = CloneVertex(ks.Center);
                    first.Rotation = ks.Rotation;
                }
                else
                {
                    VideoMotionKeyframe key = new VideoMotionKeyframe(vegas.Project, Timecode.FromNanos(ks.PositionNanos));
                    keys.Add(key);
                    key.Bounds = CloneBounds(ks.Bounds);
                    key.Center = CloneVertex(ks.Center);
                    key.Rotation = ks.Rotation;
                }
            }
        }
    }

    private static VideoMotionBounds CloneBounds(VideoMotionBounds b)
    {
        return new VideoMotionBounds(CloneVertex(b.TopLeft), CloneVertex(b.TopRight), CloneVertex(b.BottomRight), CloneVertex(b.BottomLeft));
    }
    private static VideoMotionVertex CloneVertex(VideoMotionVertex v)
    {
        return new VideoMotionVertex(v.X, v.Y);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    // ----------------------------------------------------------
    //  Dialog window
    // ----------------------------------------------------------
    public class SmartZoomWindow : Form
    {
        public static SmartZoomWindow Current;

        private Vegas vegas;
        private List<VideoEvent> allEvents;
        private List<EventSnapshot> snapshots;
        private bool loading;

        private ComboBox presetBox;
        private ComboBox easingBox;
        private ComboBox applyToBox;
        private TrackBar zoomBar;
        private TrackBar rotBar;
        private TrackBar panXBar;
        private TrackBar panYBar;
        private TrackBar beatFramesBar;
        private Label zoomLabel;
        private Label rotLabel;
        private Label panXLabel;
        private Label panYLabel;
        private Label infoLabel;
        private Label statusLabel;
        private CheckBox reverseBox;
        private CheckBox keepFullFrameBox;
        private CheckBox beatSyncBox;
        private NumericUpDown pointsBox;
        private Panel framePanel;
        private Panel curvePanel;

        private Dictionary<string, MotionSettings> presets;
        private List<string> presetNames;
        private Dictionary<string, bool> builtIn;
        private string presetFile;

        public SmartZoomWindow(Vegas v, List<VideoEvent> all, List<EventSnapshot> snaps)
        {
            vegas = v;
            allEvents = all;
            snapshots = snaps;
            Current = this;

            presets = new Dictionary<string, MotionSettings>();
            presetNames = new List<string>();
            builtIn = new Dictionary<string, bool>();

            presetFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "VegasaurToolkit", "SmartZoomPresets.txt");

            AddPreset("Zoom In 5%",   MakeSettings(5, false, 0, 0, 0, 0, 0, true, false, 10), true);
            AddPreset("Zoom Out 5%",  MakeSettings(5, true,  0, 0, 0, 0, 0, true, false, 10), true);
            AddPreset("Ken Burns",    MakeSettings(15, false, 3, 0, 5, 5, 0, true, false, 10), true);
            AddPreset("Pan Left",     MakeSettings(5, false, 0, 0, -10, 0, 0, true, false, 10), true);
            AddPreset("Pan Right",    MakeSettings(5, false, 0, 0, 10, 0, 0, true, false, 10), true);
            AddPreset("Tilt 3°",      MakeSettings(5, false, 0, 0, 0, 0, 3, true, false, 10), true);
            AddPreset("Beat Pulse",   MakeSettings(5, false, 0, 0, 0, 0, 0, true, true, 10), true);
            LoadCustomPresets();

            BuildUi();
            WriteSettings(new MotionSettings());
            Status(allEvents.Count + " video event(s) found.");
        }

        private MotionSettings MakeSettings(int zoom, bool reverse, int easing, int points,
            int panX, int panY, int rot, bool keepFullFrame, bool beatSync, int beatFrames)
        {
            MotionSettings s = new MotionSettings();
            s.ZoomPercent = zoom;
            s.Reverse = reverse;
            s.Easing = easing;
            s.Points = points;
            s.PanXPercent = panX;
            s.PanYPercent = panY;
            s.RotationDeg = rot;
            s.KeepFullFrame = keepFullFrame;
            s.BeatSync = beatSync;
            s.BeatFrames = beatFrames;
            return s;
        }

        private void AddPreset(string name, MotionSettings s, bool isBuiltIn)
        {
            presets[name] = s;
            presetNames.Add(name);
            if (isBuiltIn) builtIn[name] = true;
        }

        private void LoadCustomPresets()
        {
            if (!File.Exists(presetFile)) return;
            string[] lines = File.ReadAllLines(presetFile);
            foreach (string line in lines)
            {
                string name;
                MotionSettings s;
                if (MotionSettings.TryParse(line, out name, out s) && !presets.ContainsKey(name))
                    AddPreset(name, s, false);
            }
        }

        private void BuildUi()
        {
            Text = "Vegasaur Toolkit — Smart Zoom v4 (VEGAS Pro 2026)";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            ClientSize = new Size(700, 650);
            BackColor = Color.FromArgb(30, 30, 34);
            ForeColor = Color.Gainsboro;
            Font = new Font("Segoe UI", 9f);

            int x = Screen.PrimaryScreen.WorkingArea.Right - ClientSize.Width - 20;
            int y = Screen.PrimaryScreen.WorkingArea.Top + 40;
            Location = new Point(x, y);

            // ---- Motion group ----
            GroupBox motion = MakeGroup("Motion", 15, 15, 330, 235);
            motion.Controls.Add(MakeLabel("Preset", 15, 28, 70));
            presetBox = new ComboBox();
            presetBox.Left = 95;
            presetBox.Top = 25;
            presetBox.Width = 215;
            presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetBox.Items.Add("Custom");
            foreach (string n in presetNames) presetBox.Items.Add(n);
            presetBox.SelectedIndex = 0;
            presetBox.SelectedIndexChanged += new EventHandler(PresetChanged);
            motion.Controls.Add(presetBox);

            zoomLabel = MakeLabel("Zoom: 0%", 15, 56, 300);
            motion.Controls.Add(zoomLabel);
            zoomBar = MakeBar(1, 100, 15, 76, 300, 10);
            zoomBar.ValueChanged += new EventHandler(OnChanged);
            motion.Controls.Add(zoomBar);

            reverseBox = MakeCheck("Reverse (start zoomed, end normal)", 15, 110, 300);
            reverseBox.CheckedChanged += new EventHandler(OnChanged);
            motion.Controls.Add(reverseBox);

            keepFullFrameBox = MakeCheck("Keep full frame (no black edges)", 15, 133, 300);
            keepFullFrameBox.Checked = true;
            keepFullFrameBox.CheckedChanged += new EventHandler(OnChanged);
            motion.Controls.Add(keepFullFrameBox);

            rotLabel = MakeLabel("Rotation: 0°", 15, 158, 300);
            motion.Controls.Add(rotLabel);
            rotBar = MakeBar(-45, 45, 15, 178, 300, 15);
            rotBar.ValueChanged += new EventHandler(OnChanged);
            motion.Controls.Add(rotBar);

            Controls.Add(motion);

            // ---- Pan group ----
            GroupBox pan = MakeGroup("Pan", 15, 260, 330, 130);
            panXLabel = MakeLabel("Pan X: 0%", 15, 25, 300);
            pan.Controls.Add(panXLabel);
            panXBar = MakeBar(-50, 50, 15, 45, 300, 10);
            panXBar.ValueChanged += new EventHandler(OnChanged);
            pan.Controls.Add(panXBar);
            panYLabel = MakeLabel("Pan Y: 0%", 15, 78, 300);
            pan.Controls.Add(panYLabel);
            panYBar = MakeBar(-50, 50, 15, 98, 300, 10);
            panYBar.ValueChanged += new EventHandler(OnChanged);
            pan.Controls.Add(panYBar);
            Controls.Add(pan);

            // ---- Timing group ----
            GroupBox timing = MakeGroup("Timing and target", 15, 400, 330, 185);
            timing.Controls.Add(MakeLabel("Easing", 15, 28, 70));
            easingBox = new ComboBox();
            easingBox.Left = 95;
            easingBox.Top = 25;
            easingBox.Width = 215;
            easingBox.DropDownStyle = ComboBoxStyle.DropDownList;
            easingBox.Items.Add("Linear");
            easingBox.Items.Add("Ease In");
            easingBox.Items.Add("Ease Out");
            easingBox.Items.Add("Ease In Out");
            easingBox.SelectedIndex = 0;
            easingBox.SelectedIndexChanged += new EventHandler(OnChanged);
            timing.Controls.Add(easingBox);

            timing.Controls.Add(MakeLabel("Points", 15, 62, 70));
            pointsBox = new NumericUpDown();
            pointsBox.Left = 95;
            pointsBox.Top = 58;
            pointsBox.Width = 80;
            pointsBox.Minimum = 0;
            pointsBox.Maximum = 40;
            pointsBox.Value = 0;
            pointsBox.ValueChanged += new EventHandler(OnChanged);
            timing.Controls.Add(pointsBox);

            timing.Controls.Add(MakeLabel("Apply to", 15, 96, 70));
            applyToBox = new ComboBox();
            applyToBox.Left = 95;
            applyToBox.Top = 93;
            applyToBox.Width = 215;
            applyToBox.DropDownStyle = ComboBoxStyle.DropDownList;
            applyToBox.Items.Add("Auto (selected, else all)");
            applyToBox.Items.Add("Selected only");
            applyToBox.Items.Add("All video events");
            applyToBox.SelectedIndex = 0;
            applyToBox.SelectedIndexChanged += new EventHandler(OnChanged);
            timing.Controls.Add(applyToBox);

            beatSyncBox = MakeCheck("Beat pulse", 15, 128, 80);
            beatSyncBox.CheckedChanged += new EventHandler(BeatSyncChanged);
            timing.Controls.Add(beatSyncBox);

            timing.Controls.Add(MakeLabel("Every", 95, 130, 40));
            beatFramesBar = MakeBar(2, 120, 140, 126, 70, 10);
            beatFramesBar.Value = 10;
            beatFramesBar.ValueChanged += new EventHandler(OnChanged);
            timing.Controls.Add(beatFramesBar);
            timing.Controls.Add(MakeLabel("frames", 215, 130, 60));

            Controls.Add(timing);

            // ---- Right side: preview + curve ----
            Controls.Add(MakeLabel("Frame preview (gray = start, blue = end)", 360, 15, 300));
            framePanel = new Panel();
            framePanel.Left = 360;
            framePanel.Top = 38;
            framePanel.Width = 320;
            framePanel.Height = 170;
            framePanel.BackColor = Color.FromArgb(18, 18, 22);
            framePanel.Paint += new PaintEventHandler(FramePanel_Paint);
            Controls.Add(framePanel);

            Controls.Add(MakeLabel("Easing curve", 360, 222, 290));
            curvePanel = new Panel();
            curvePanel.Left = 360;
            curvePanel.Top = 243;
            curvePanel.Width = 320;
            curvePanel.Height = 120;
            curvePanel.BackColor = Color.FromArgb(18, 18, 22);
            curvePanel.Paint += new PaintEventHandler(CurvePanel_Paint);
            Controls.Add(curvePanel);

            infoLabel = MakeLabel("", 360, 375, 320);
            infoLabel.Height = 110;
            Controls.Add(infoLabel);

            // ---- Buttons ----
            Button previewBtn = MakeButton("Preview", 15, 600, 110);
            previewBtn.Click += new EventHandler(PreviewBtn_Click);
            Controls.Add(previewBtn);

            Button saveBtn = MakeButton("Save Preset...", 135, 600, 115);
            saveBtn.Click += new EventHandler(SaveBtn_Click);
            Controls.Add(saveBtn);

            Button applyBtn = MakeButton("Apply Effect", 260, 600, 110);
            applyBtn.Click += new EventHandler(ApplyBtn_Click);
            Controls.Add(applyBtn);

            Button resetBtn = MakeButton("Reset Original", 380, 600, 110);
            resetBtn.Click += new EventHandler(ResetBtn_Click);
            Controls.Add(resetBtn);

            Button exitBtn = MakeButton("Exit", 500, 600, 180);
            exitBtn.Click += new EventHandler(ExitBtn_Click);
            Controls.Add(exitBtn);
        }

        private GroupBox MakeGroup(string title, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = title;
            g.Left = x;
            g.Top = y;
            g.Width = w;
            g.Height = h;
            g.ForeColor = Color.White;
            return g;
        }

        private Label MakeLabel(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.Left = x;
            l.Top = y + 2;
            l.Width = w;
            l.ForeColor = Color.Gainsboro;
            return l;
        }

        private TrackBar MakeBar(int min, int max, int x, int y, int w, int tick)
        {
            TrackBar t = new TrackBar();
            t.Minimum = min;
            t.Maximum = max;
            t.TickFrequency = tick;
            t.Left = x;
            t.Top = y;
            t.Width = w;
            t.BackColor = Color.FromArgb(30, 30, 34);
            return t;
        }

        private CheckBox MakeCheck(string text, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Left = x;
            c.Top = y;
            c.Width = w;
            c.ForeColor = Color.Gainsboro;
            return c;
        }

        private Button MakeButton(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text;
            b.Left = x;
            b.Top = y;
            b.Width = w;
            b.Height = 34;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(55, 55, 64);
            b.ForeColor = Color.White;
            return b;
        }

        // ----------------------------------------------------------
        //  Event handlers
        // ----------------------------------------------------------
        private void PresetChanged(object sender, EventArgs e)
        {
            if (loading) return;
            if (presetBox.SelectedIndex > 0)
            {
                string name = presetBox.SelectedItem.ToString();
                WriteSettings(presets[name]);
            }
        }

        private void BeatSyncChanged(object sender, EventArgs e)
        {
            if (loading) return;
            pointsBox.Enabled = !beatSyncBox.Checked;
            Refresh3();
        }

        private void OnChanged(object sender, EventArgs e)
        {
            if (loading) return;
            Refresh3();
        }

        private void PreviewBtn_Click(object sender, EventArgs e)
        {
            ApplyCurrent("Preview applied");
        }

        private void SaveBtn_Click(object sender, EventArgs e)
        {
            SavePreset();
        }

        private void ApplyBtn_Click(object sender, EventArgs e)
        {
            ApplyCurrent("Applied");
        }

        private void ResetBtn_Click(object sender, EventArgs e)
        {
            RestoreSnapshots(vegas, snapshots);
            Text = "Vegasaur Toolkit — Smart Zoom — Reset";
        }

        private void ExitBtn_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void FramePanel_Paint(object sender, PaintEventArgs e)
        {
            DrawFrame(e.Graphics, framePanel.ClientRectangle);
        }

        private void CurvePanel_Paint(object sender, PaintEventArgs e)
        {
            DrawCurve(e.Graphics, curvePanel.ClientRectangle);
        }

        // ----------------------------------------------------------
        //  Logic
        // ----------------------------------------------------------
        private void Refresh3()
        {
            UpdateLabels();
            if (framePanel != null) framePanel.Invalidate();
            if (curvePanel != null) curvePanel.Invalidate();
        }

        private void UpdateLabels()
        {
            if (zoomLabel == null) return;
            MotionSettings s = ReadSettings();
            zoomLabel.Text = "Zoom: " + s.ZoomPercent + "%";
            rotLabel.Text = "Rotation: " + s.RotationDeg + "°";
            panXLabel.Text = "Pan X: " + s.PanXPercent + "%";
            panYLabel.Text = "Pan Y: " + s.PanYPercent + "%";

            string[] easingNames = { "Linear", "Ease In", "Ease Out", "Ease In Out" };
            int easingIdx = Clamp(s.Easing, 0, 3);
            string dir = s.Reverse ? "zoom out / reverse" : "zoom in";
            string keep = s.KeepFullFrame ? "keep full frame" : "allow true zoom out";
            string beat = s.BeatSync ? ("beat pulse every " + s.BeatFrames + " frames") : "no beat pulse";
            infoLabel.Text = "Mode: " + dir + ", " + keep + ", " +
                easingNames[easingIdx] +
                ", " + s.Points + " intermediate point(s), " + beat + ".\n\n" +
                "Preview applies these settings to the timeline. Apply keeps them; Reset restores the original motion.";
        }

        private void Status(string text)
        {
            if (statusLabel != null) statusLabel.Text = text;
        }

        private MotionSettings ReadSettings()
        {
            MotionSettings s = new MotionSettings();
            s.ZoomPercent = zoomBar.Value;
            s.Reverse = reverseBox.Checked;
            s.Easing = (easingBox.SelectedIndex < 0) ? 0 : easingBox.SelectedIndex;
            s.Points = (int)pointsBox.Value;
            s.PanXPercent = panXBar.Value;
            s.PanYPercent = panYBar.Value;
            s.RotationDeg = rotBar.Value;
            s.ApplyTo = (applyToBox.SelectedIndex < 0) ? 0 : applyToBox.SelectedIndex;
            s.KeepFullFrame = keepFullFrameBox.Checked;
            s.BeatSync = beatSyncBox.Checked;
            s.BeatFrames = (int)beatFramesBar.Value;
            return s;
        }

        private void WriteSettings(MotionSettings s)
        {
            loading = true;
            zoomBar.Value = Clamp(s.ZoomPercent, zoomBar.Minimum, zoomBar.Maximum);
            reverseBox.Checked = s.Reverse;
            easingBox.SelectedIndex = Clamp(s.Easing, 0, 3);
            pointsBox.Value = Clamp((int)s.Points, 0, 40);
            panXBar.Value = Clamp(s.PanXPercent, -50, 50);
            panYBar.Value = Clamp(s.PanYPercent, -50, 50);
            rotBar.Value = Clamp(s.RotationDeg, -45, 45);
            applyToBox.SelectedIndex = Clamp(s.ApplyTo, 0, 2);
            keepFullFrameBox.Checked = s.KeepFullFrame;
            beatSyncBox.Checked = s.BeatSync;
            beatFramesBar.Value = Clamp(s.BeatFrames, 2, 120);
            pointsBox.Enabled = !s.BeatSync;
            loading = false;
            Refresh3();
        }

        private void ApplyCurrent(string statusText)
        {
            try
            {
                MotionSettings s = ReadSettings();
                RestoreSnapshots(vegas, snapshots);
                int changed = 0;
                List<VideoEvent> targets = GetTargets(allEvents, s.ApplyTo);
                foreach (VideoEvent ev in targets)
                {
                    if (ApplyMotion(vegas, ev, s)) changed++;
                }
                Text = "Vegasaur Toolkit — Smart Zoom — " + statusText + " (" + changed + ")";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error: " + ex.Message, "Vegasaur Toolkit");
            }
        }

        private void SavePreset()
        {
            string name = PromptName("My preset");
            if (string.IsNullOrEmpty(name)) return;
            if (name.Contains("|") || builtIn.ContainsKey(name))
            {
                MessageBox.Show(this, "This name is invalid or reserved.", "Vegasaur Toolkit");
                return;
            }
            if (!presets.ContainsKey(name)) presetNames.Add(name);
            presets[name] = ReadSettings();
            try
            {
                string dir = Path.GetDirectoryName(presetFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                List<string> lines = new List<string>();
                foreach (string n in presetNames)
                {
                    if (!builtIn.ContainsKey(n)) lines.Add(presets[n].ToLine(n));
                }
                File.WriteAllLines(presetFile, lines.ToArray());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save preset: " + ex.Message, "Vegasaur Toolkit");
            }
            loading = true;
            presetBox.SelectedItem = name;
            loading = false;
        }

        private string PromptName(string initial)
        {
            using (Form f = new Form())
            {
                f.Text = "Preset name";
                f.Width = 330;
                f.Height = 140;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Color.FromArgb(30, 30, 34);
                f.ForeColor = Color.Gainsboro;

                TextBox tb = new TextBox();
                tb.Left = 12;
                tb.Top = 15;
                tb.Width = 290;
                tb.Text = initial;

                Button ok = new Button();
                ok.Text = "OK";
                ok.Left = 120;
                ok.Top = 55;
                ok.Width = 90;
                ok.Height = 30;
                ok.DialogResult = DialogResult.OK;

                f.Controls.Add(tb);
                f.Controls.Add(ok);
                f.AcceptButton = ok;

                DialogResult result = f.ShowDialog(this);
                if (result == DialogResult.OK)
                    return tb.Text.Trim();
                return null;
            }
        }

        // ----------------------------------------------------------
        //  Custom drawing
        // ----------------------------------------------------------
        private void DrawFrame(Graphics g, Rectangle area)
        {
            Rectangle frame = Rectangle.Inflate(area, -20, -20);
            using (Pen border = new Pen(Color.FromArgb(90, 90, 100)))
                g.DrawRectangle(border, frame);

            MotionSettings s = ReadSettings();
            double zoom = 1.0 + s.ZoomPercent / 100.0;
            double zoomed = s.KeepFullFrame ? (1.0 / zoom) : zoom;
            double startScale = s.Reverse ? zoomed : 1.0;
            double endScale = s.Reverse ? 1.0 : zoomed;
            double px = s.PanXPercent / 100.0;
            double py = s.PanYPercent / 100.0;
            double startPx = s.Reverse ? px : 0.0;
            double startPy = s.Reverse ? py : 0.0;
            double endPx = s.Reverse ? 0.0 : px;
            double endPy = s.Reverse ? 0.0 : py;

            DrawRect(g, frame, startScale, startPx, startPy, Color.Gray, true);
            DrawRect(g, frame, endScale, endPx, endPy, Color.DeepSkyBlue, false);
        }

        private void DrawRect(Graphics g, Rectangle frame, double scale, double px, double py, Color color, bool dashed)
        {
            double cx = frame.Left + frame.Width / 2.0 + px * frame.Width;
            double cy = frame.Top + frame.Height / 2.0 + py * frame.Height;
            double w = frame.Width * scale;
            double h = frame.Height * scale;
            Rectangle r = new Rectangle((int)(cx - w / 2.0), (int)(cy - h / 2.0), (int)w, (int)h);
            using (Pen pen = new Pen(color, 2f))
            {
                if (dashed) pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, r);
            }
        }

        private void DrawCurve(Graphics g, Rectangle area)
        {
            Rectangle inner = Rectangle.Inflate(area, -15, -15);
            int n = 60;
            int easing = Clamp(easingBox.SelectedIndex, 0, 3);
            Point[] pts = new Point[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double v = MotionSettings.Ease(t, easing);
                pts[i] = new Point(inner.Left + (int)(t * inner.Width), inner.Bottom - (int)(v * inner.Height));
            }
            using (Pen axis = new Pen(Color.FromArgb(70, 70, 80)))
            {
                g.DrawLine(axis, inner.Left, inner.Bottom, inner.Right, inner.Bottom);
                g.DrawLine(axis, inner.Left, inner.Top, inner.Left, inner.Bottom);
            }
            using (Pen curve = new Pen(Color.DeepSkyBlue, 2f))
                g.DrawLines(curve, pts);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Current = null;
            base.OnFormClosing(e);
        }
    }

    // ============================================================
    //  Settings model
    // ============================================================
    public class MotionSettings
    {
        public int ZoomPercent = 5;
        public bool Reverse = false;
        public int Easing = 0;
        public int Points = 0;
        public int PanXPercent = 0;
        public int PanYPercent = 0;
        public int RotationDeg = 0;
        public int ApplyTo = 0;
        public bool KeepFullFrame = true;
        public bool BeatSync = false;
        public int BeatFrames = 10;

        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        public static double Ease(double t, int easing)
        {
            switch (easing)
            {
                case 1: return t * t;
                case 2: return 1.0 - Math.Pow(1.0 - t, 2.0);
                case 3: return t < 0.5 ? 2.0 * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 2.0) / 2.0;
                default: return t;
            }
        }

        public string ToLine(string name)
        {
            List<string> parts = new List<string>();
            parts.Add(name);
            parts.Add(ZoomPercent.ToString());
            parts.Add(Reverse ? "1" : "0");
            parts.Add(Easing.ToString());
            parts.Add(Points.ToString());
            parts.Add(PanXPercent.ToString());
            parts.Add(PanYPercent.ToString());
            parts.Add(RotationDeg.ToString());
            parts.Add(ApplyTo.ToString());
            parts.Add(KeepFullFrame ? "1" : "0");
            parts.Add(BeatSync ? "1" : "0");
            parts.Add(BeatFrames.ToString());
            return string.Join("|", parts.ToArray());
        }

        public static bool TryParse(string line, out string name, out MotionSettings s)
        {
            name = null;
            s = null;
            string[] p = line.Split('|');
            if (p.Length != 12) return false;

            int zoom, easing, points, px, py, rot, applyTo;
            if (!int.TryParse(p[1], out zoom)) return false;
            if (!int.TryParse(p[3], out easing)) return false;
            if (!int.TryParse(p[4], out points)) return false;
            if (!int.TryParse(p[5], out px)) return false;
            if (!int.TryParse(p[6], out py)) return false;
            if (!int.TryParse(p[7], out rot)) return false;
            if (!int.TryParse(p[8], out applyTo)) return false;

            name = p[0];
            s = new MotionSettings();
            s.ZoomPercent = zoom;
            s.Reverse = (p[2] == "1");
            s.Easing = easing;
            s.Points = points;
            s.PanXPercent = px;
            s.PanYPercent = py;
            s.RotationDeg = rot;
            s.ApplyTo = applyTo;
            s.KeepFullFrame = (p[9] == "1");
            s.BeatSync = (p[10] == "1");
            int bf;
            if (int.TryParse(p[11], out bf)) s.BeatFrames = bf;
            return true;
        }
    }

    // ============================================================
    //  Snapshot helpers
    // ============================================================
    public class EventSnapshot
    {
        public VideoEvent Event;
        public List<KeyframeSnapshot> Keyframes = new List<KeyframeSnapshot>();
    }
    public class KeyframeSnapshot
    {
        public long PositionNanos;
        public VideoMotionBounds Bounds;
        public VideoMotionVertex Center;
        public double Rotation;
    }
}
