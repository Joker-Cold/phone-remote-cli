using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RemoteCli {
/// What the phone may ask of the program itself, beside its terminals: to be told a few settings, and to change one
/// of them (docs/PROTOCOL.md, "The program itself"). The password and the port are changed at the computer only.
public sealed partial class App {
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static readonly string[] Rights = { "full", "edit", "read" };

    // ---------- settings: changed here, from the window and from the phone alike
    bool Autostart {
        get { using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue("RemoteCli") != null; }
        set { using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) { if (value) key.SetValue("RemoteCli", "\"" + Application.ExecutablePath + "\" --hidden"); else key.DeleteValue("RemoteCli", false); } }
    }
    // The program is portable: after its folder was moved, "start with Windows" points at where it runs now.
    void AutostartFromHere() { if (Autostart) Autostart = true; }
    void Set(string name, object value) {
        string word = value as string;
        if (name == "rights" && Array.IndexOf(Rights, word) >= 0) Keep("RemoteMaxMode", word);
        else if (name == "autostart" && value is bool) Autostart = (bool)value;
        else throw new ArgumentException("没有这项设置，或它的值无效");
        if (InvokeRequired) BeginInvoke(new Action(ShowSettings)); else ShowSettings();
    }
    void ShowSettings() {
        rights.Chosen = Math.Max(0, Array.IndexOf(Rights, Convert.ToString(config["RemoteMaxMode"])));
        autostartSwitch.On = Autostart;
    }

    // ---------- what the phone is told
    Dictionary<string, object> About() {
        return new Dictionary<string, object> { { "name", ComputerName }, { "version", Version },
            { "settings", new Dictionary<string, object> { { "rights", Convert.ToString(config["RemoteMaxMode"]) }, { "autostart", Autostart } } } };
    }
    static string Word(Dictionary<string, object> op, string key) { object value; return op.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : ""; }
    // Called by the agent, on its thread, for each question.
    Dictionary<string, object> Itself(Dictionary<string, object> op) {
        if (Word(op, "action") == "settings_change") { object value; op.TryGetValue("value", out value); Set(Word(op, "name"), value); }
        return About();
    }
}
}
