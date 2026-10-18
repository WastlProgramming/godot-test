using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using JumpRun.Audio;

namespace JumpRun.Tests;

/// <summary>
/// Headless audio test (TASKS_V2 A4): AudioManager (autoload or local instance) + AudioEventBridge on a dummy
/// player that declares the §5.3 signals as user signals. Fires every signal, checks streams, loops, bus
/// volumes, voice pool and disconnect. Prints <c>AUDIO_TEST_PASS</c> and exits 0 on success.
/// </summary>
public partial class AudioTest : Node
{
    private readonly List<string> _failures = new();
    private AudioManager? _am;
    private AudioEventBridge? _bridge;
    private Node3D? _dummy;
    private Node? _inventory;
    private int _phase;
    private float _t;
    private float _total;

    public override void _Ready()
    {
        GD.Print("[AudioTest] start");
        try
        {
            _am = AudioManager.Instance ?? GetNodeOrNull<AudioManager>("/root/AudioManager");
            if (_am == null)
            {
                _am = new AudioManager { Name = "AudioManager" };
                AddChild(_am);
            }
            _am.PlaybackEnabled = true; // headless default is off (no leaked playbacks at Quit)
            Check(AudioManager.Instance == _am, "AudioManager.Instance gesetzt");
        }
        catch (Exception e) { Fail("Setup: " + e); }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        _total += dt;
        if (_total > 30f && _phase < 4) { Fail("Timeout"); Finish(); return; }
        if (Time.GetTicksMsec() - _startMs > 60_000) { Fail("Timeout (Echtzeit)"); Finish(); return; }
        try
        {
            switch (_phase)
            {
                case 0:
                    if (_t < 0.1f) return;
                    CheckStreams();
                    CheckVolumes();
                    CheckMusic();
                    SetupBridge();
                    Next();
                    break;
                case 1:
                    // wait past the bridge's startup mute window
                    if (_t < AudioEventBridge.StartupMuteSeconds + 0.2f) return;
                    FireSignals();
                    Next();
                    break;
                case 2:
                    if (_t < 0.3f) return;
                    Check(_am!.IsLoopActive(Sfx.HelicopterLoop), "helicopter_loop läuft nach FlightStarted");
                    _dummy!.EmitSignal("FlightEnded");
                    _dummy.EmitSignal("FlightEnded"); // idempotent
                    Check(!_am.IsLoopActive(Sfx.HelicopterLoop), "helicopter_loop gestoppt nach FlightEnded");
                    CheckPoolAndUnknown();
                    CheckButton();
                    _bridge!.QueueFree();
                    Next();
                    break;
                case 3:
                    if (_t < 0.1f) return;
                    int left = _dummy!.GetSignalConnectionList("Jumped").Count
                               + _dummy.GetSignalConnectionList("ItemUsed").Count
                               + _inventory!.GetSignalConnectionList("ItemAdded").Count;
                    Check(left == 0, $"Bridge trennt Signale in _ExitTree (übrig: {left})");
                    _am!.StopMusic(0f);
                    Check(_am.CurrentMusic == null, "StopMusic");
                    _am.StopAll();
                    _stopAllMs = Time.GetTicksMsec();
                    Next();
                    break;
                case 4:
                    // give the audio server a few mix cycles to release the stopped playbacks before quitting;
                    // real time, because --fixed-fps runs frames faster than the audio thread mixes
                    if (_t < 0.25f || Time.GetTicksMsec() - _stopAllMs < 400) return;
                    Finish();
                    break;
            }
        }
        catch (Exception e)
        {
            Fail($"Exception in Phase {_phase}: {e}");
            Finish();
        }
    }

    private ulong _stopAllMs;
    private readonly ulong _startMs = Time.GetTicksMsec();

    private void Next() { _phase++; _t = 0f; }

    private void CheckStreams()
    {
        foreach (string n in Sfx.All)
        {
            AudioStream? s = _am!.GetStream(n);
            Check(s != null, $"Stream '{n}' ladbar");
            if (s != null && !Sfx.IsLoop(n))
                Check(s.GetLength() > 0.01 && s.GetLength() <= 2.01, $"'{n}' Länge {s.GetLength():0.000}s ≤ 2 s");
        }
        foreach (string n in new[] { Sfx.MusicMain, Sfx.HelicopterLoop })
        {
            AudioStream? s = _am!.GetStream(n, forceLoop: true);
            if (s is AudioStreamWav w)
            {
                int frames = (int)(w.GetLength() * w.MixRate);
                Check(w.LoopMode == AudioStreamWav.LoopModeEnum.Forward, $"'{n}' LoopMode Forward");
                Check(w.LoopBegin == 0 && Math.Abs(w.LoopEnd - frames) <= 1 && frames > 0,
                    $"'{n}' LoopBegin 0 / LoopEnd {w.LoopEnd} == {frames}");
            }
            else Fail($"'{n}' ist kein AudioStreamWav");
        }
        AudioStream? music = _am!.GetStream(Sfx.MusicMain);
        Check(music != null && music.GetLength() >= 60 && music.GetLength() <= 90, "music_main 60..90 s");
        AudioStream? jump = _am.GetStream(Sfx.Jump);
        Check(jump is AudioStreamWav jw && jw.LoopMode == AudioStreamWav.LoopModeEnum.Disabled, "jump loopt nicht");
    }

    private void CheckVolumes()
    {
        Check(AudioServer.GetBusIndex("Music") >= 0 && AudioServer.GetBusIndex("SFX") >= 0, "Busse Music/SFX existieren");
        Check(AudioServer.GetBusSend(AudioServer.GetBusIndex("Music")) == "Master", "Music → Master");
        Check(AudioServer.GetBusSend(AudioServer.GetBusIndex("SFX")) == "Master", "SFX → Master");
        (string bus, Action<float> set, Func<float> get)[] vols =
        {
            ("Master", v => _am!.MasterVolume = v, () => _am!.MasterVolume),
            ("Music", v => _am!.MusicVolume = v, () => _am!.MusicVolume),
            ("SFX", v => _am!.SfxVolume = v, () => _am!.SfxVolume),
        };
        foreach (var (bus, set, get) in vols)
        {
            int idx = AudioServer.GetBusIndex(bus);
            set(0f);
            Check(AudioServer.IsBusMute(idx) && AudioServer.GetBusVolumeDb(idx) <= -79f, $"{bus} 0 → stumm");
            set(0.5f);
            Check(!AudioServer.IsBusMute(idx) && Mathf.Abs(AudioServer.GetBusVolumeDb(idx) - (-6.0206f)) < 0.05f,
                $"{bus} 0.5 → -6.02 dB (ist {AudioServer.GetBusVolumeDb(idx):0.00})");
            set(1f);
            Check(Mathf.Abs(AudioServer.GetBusVolumeDb(idx)) < 0.01f, $"{bus} 1 → 0 dB");
            set(2f);
            Check(Mathf.IsEqualApprox(get(), 1f), $"{bus} geklemmt auf 1");
        }
        _am!.MasterVolume = 1f;
        _am.MusicVolume = 0.7f;
        _am.SfxVolume = 0.8f;
    }

    private void CheckMusic()
    {
        _am!.PlayMusic();
        Check(_am.CurrentMusic == Sfx.MusicMain, "music_main läuft");
        _am.PlayMusic(Sfx.MusicMain, 0.2f); // same track again → no restart, no error
        Check(_am.CurrentMusic == Sfx.MusicMain, "PlayMusic idempotent");
    }

    private void SetupBridge()
    {
        _dummy = new Node3D { Name = "DummyPlayer" };
        foreach (string s in new[] { "Jumped", "Landed", "Respawned", "Attacked", "GrappleStarted", "GrappleEnded",
                     "FlightStarted", "FlightEnded", "Bounced" })
            _dummy.AddUserSignal(s);
        _dummy.AddUserSignal("CrateHit", Args(("crate", Variant.Type.Object)));
        _dummy.AddUserSignal("CheckpointReached", Args(("chunkIndex", Variant.Type.Int)));
        _dummy.AddUserSignal("ItemUseFailed", Args(("reason", Variant.Type.String)));
        _dummy.AddUserSignal("ItemUsed", Args(("itemType", Variant.Type.Int)));
        _inventory = new Node { Name = "Inventory" };
        _inventory.AddUserSignal("ItemAdded", Args(("itemType", Variant.Type.Int), ("newCount", Variant.Type.Int)));
        _dummy.AddChild(_inventory);
        AddChild(_dummy);

        _bridge = new AudioEventBridge { Name = "AudioEventBridge", ExplicitTarget = _dummy };
        AddChild(_bridge);
    }

    private static Godot.Collections.Array Args(params (string name, Variant.Type type)[] args)
    {
        var a = new Godot.Collections.Array();
        foreach (var (name, type) in args)
            a.Add(new Dictionary { { "name", name }, { "type", (int)type } });
        return a;
    }

    private void FireSignals()
    {
        Check(_bridge!.Target == _dummy, "Bridge hat Dummy gefunden");
        Check(_bridge.ConnectedCount == 14, $"Bridge verbunden mit 14 Signalen (ist {_bridge.ConnectedCount})");
        var crate = new Node3D { Name = "Crate", Position = new Vector3(1, 2, 3) };
        AddChild(crate);
        _dummy!.EmitSignal("Jumped");
        _dummy.EmitSignal("Landed");
        _dummy.EmitSignal("Landed"); // throttled
        _dummy.EmitSignal("Attacked");
        _dummy.EmitSignal("CrateHit", crate);
        _dummy.EmitSignal("CrateHit", default(Variant));
        for (int i = 0; i <= 5; i++) _dummy.EmitSignal("ItemUsed", i);
        _dummy.EmitSignal("GrappleStarted");
        _dummy.EmitSignal("GrappleEnded");
        _dummy.EmitSignal("GrappleStarted");
        _dummy.EmitSignal("Bounced");
        _dummy.EmitSignal("Respawned");
        _dummy.EmitSignal("CheckpointReached", 0);
        _dummy.EmitSignal("CheckpointReached", 3);
        _dummy.EmitSignal("ItemUseFailed", "kein Item");
        _inventory!.EmitSignal("ItemAdded", 1, 7);
        _dummy.EmitSignal("FlightStarted");
        _dummy.EmitSignal("FlightStarted"); // idempotent loop
        crate.QueueFree();
    }

    private void CheckPoolAndUnknown()
    {
        int before = _am!.WarnedCount;
        _am.PlaySfx("does_not_exist");
        _am.PlaySfx("does_not_exist");
        _am.PlaySfx("does_not_exist", Vector3.Zero);
        Check(_am.WarnedCount == before + 1, "unbekannter Name nur einmal gewarnt");
        for (int i = 0; i < 30; i++) _am.PlaySfx(Sfx.Pickup);
        for (int i = 0; i < 20; i++) _am.PlaySfx(Sfx.CrateBreak, new Vector3(i, 0, 0));
        Check(_am.ActiveVoices2D <= AudioManager.Pool2DSize, "2D-Pool begrenzt (Voice-Stealing)");
        _am.SetLoop("does_not_exist", true);
        _am.SetLoop("does_not_exist", false);
    }

    private void CheckButton()
    {
        var b = new Button { Text = "Test" };
        AddChild(b);
        b.EmitSignal(BaseButton.SignalName.Pressed);
        b.EmitSignal(Control.SignalName.MouseEntered);
        b.QueueFree();
    }

    private void Check(bool ok, string what)
    {
        if (ok) GD.Print("  ok   " + what);
        else Fail(what);
    }

    private void Fail(string what)
    {
        _failures.Add(what);
        GD.PrintErr("  FAIL " + what);
    }

    private void Finish()
    {
        SetProcess(false);
        if (_failures.Count == 0)
        {
            GD.Print("AUDIO_TEST_PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"AUDIO_TEST_FAIL ({_failures.Count}): {_failures[0]}");
            GetTree().Quit(1);
        }
    }
}
