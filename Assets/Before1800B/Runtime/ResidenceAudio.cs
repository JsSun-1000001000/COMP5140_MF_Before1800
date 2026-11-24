using System;
using UnityEngine;

namespace Before1800.PartB
{
    /// <summary>
    /// An imported original score and a foley bank generated once.
    /// The score follows uncertainty rather than declaring Lin's emotions for her.
    /// No synthesis, arrays, scene searches or managed allocations occur per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResidenceAudio : MonoBehaviour
    {
        const int SampleRate = 22050;
        const float BarSeconds = 3f;
        const float ScoreSeconds = 24f;
        const string MasterKey = "Before1800.Audio.Master";
        const string MusicKey = "Before1800.Audio.Music";
        const string MuteKey = "Before1800.Audio.Muted";

        PartBScene scene;
        Transform audioRoot;
        AudioSource music, tension, room, foley, playerFeet, linFeet;
        AudioClip[] ownedClips;
        AudioClip[] footsteps;
        AudioClip paper, touch, confirmed, error, greeting, closing, leaving;
        AudioSource[] authoredSources;
        float[] authoredVolumes;
        Guid run;
        Vector3 playerPosition, linPosition;
        float playerStride, linStride, playerCooldown, linCooldown, mix, tensionMix;
        float cueCooldown, transitionCooldown;
        int stepIndex, linStepIndex, evidenceMask;
        bool started, wasDialogue, wasJournal, wasIntro, wasModelWait;
        string previousEnding = "none";
        float masterVolume = .8f, musicVolume = .55f;
        bool muted;
        bool rebuildPending; float healthCheck, previewUntil;
        readonly float[] outputProbe = new float[1024];
        bool outputReported; int outputChecks;
        public bool MusicPlaying => music && music.isPlaying;
        public int RecoveryCount { get; private set; }

        /// <summary>Controls this score/foley and the scene's existing authored sound sources.</summary>
        public float MasterVolume
        {
            get => masterVolume;
            set
            {
                float next = Mathf.Clamp01(value);
                if (Mathf.Approximately(next, masterVolume)) return;
                masterVolume = next;
                PlayerPrefs.SetFloat(MasterKey, next);
            }
        }

        public float MusicVolume
        {
            get => musicVolume;
            set
            {
                float next = Mathf.Clamp01(value);
                if (Mathf.Approximately(next, musicVolume)) return;
                musicVolume = next;
                PlayerPrefs.SetFloat(MusicKey, next);
            }
        }

        public bool Muted
        {
            get => muted;
            set
            {
                if (muted == value) return;
                muted = value;
                PlayerPrefs.SetInt(MuteKey, value ? 1 : 0);
            }
        }

        void Awake()
        {
            scene = GetComponent<PartBScene>();
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, .8f));
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, .55f));
            muted = PlayerPrefs.GetInt(MuteKey, 0) != 0;
            audioRoot = new GameObject("Original score & room sounds").transform;
            audioRoot.SetParent(transform, false);
            music = Source("Letters — felt keys", false);
            tension = Source("What the walls remember", false);
            room = Source("Quiet apartment", false);
            foley = Source("Small interactions", false);
            playerFeet = Source("Player footsteps", false);
            linFeet = Source("Lin footsteps", true);
            BuildBank();
        }

        void BuildBank()
        {
            if (ownedClips != null) foreach (var clip in ownedClips) if (clip) Destroy(clip);
            footsteps = new AudioClip[4];
            ownedClips = new AudioClip[14];
            music.clip = Resources.Load<AudioClip>("Audio/LastLetter");
            if (!music.clip) music.clip = ownedClips[0] = Score(false);
            tension.clip = ownedClips[1] = Score(true);
            room.clip = ownedClips[2] = RoomTone();
            paper = ownedClips[3] = Sound("Paper unfolding", .48f, 0);
            touch = ownedClips[4] = Sound("Soft wooden click", .11f, 1);
            error = ownedClips[5] = Sound("An unanswered question", .36f, 2);
            greeting = ownedClips[6] = Sound("A moment to listen", .65f, 3);
            confirmed = ownedClips[7] = Sound("Something witnessed", 1.65f, 4);
            closing = ownedClips[8] = Sound("An open future", 4f, 5);
            leaving = ownedClips[9] = Sound("The last delivery", 3.5f, 6);
            for (int i = 0; i < footsteps.Length; i++)
                footsteps[i] = ownedClips[10 + i] = Footstep(i);
            music.loop = tension.loop = room.loop = true;
            music.ignoreListenerPause = tension.ignoreListenerPause = room.ignoreListenerPause = true;
            foreach (var sound in new[]{foley,playerFeet,linFeet}) sound.ignoreListenerPause=true;
        }

        // Device changes discard procedural clip buffers and scheduled playback.
        // Recreate those buffers on the main thread; imported music stays on disk.
        void AudioChanged(bool deviceChanged) { rebuildPending=true; }
        public void RecoverAudio()
        {
            if (!started) return;
            BuildBank(); StartScore(); RecoveryCount++; rebuildPending=false; outputReported=false;
            Debug.Log("[Audio] Restored music and foley after output change.");
        }
        public void PreviewMusic()
        {
            GetComponent<LinVoice>()?.Stop();
            Muted=false;if(MasterVolume<.2f)MasterVolume=.8f;if(MusicVolume<.2f)MusicVolume=.65f;
            RecoverAudio(); previewUntil=Time.unscaledTime+6f;
        }
        public void PreviewEffects() { GetComponent<LinVoice>()?.Stop(); Muted=false; PlayCue("paper"); }

        AudioSource Source(string name, bool spatial)
        {
            var child = new GameObject(name);
            child.transform.SetParent(audioRoot, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = 0f;
            source.spatialBlend = spatial ? 1f : 0f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 8f;
            source.priority = spatial ? 160 : 128;
            return source;
        }

        void Start()
        {
            if (!scene || scene.State == null || !scene.World)
            {
                enabled = false;
                return;
            }
            // Preserve authored door/clock/television recordings; only scale their volume.
            authoredSources = GetComponentsInChildren<AudioSource>(true);
            authoredVolumes = new float[authoredSources.Length];
            for (int i = 0; i < authoredSources.Length; i++)
                authoredVolumes[i] = (authoredSources[i].transform.IsChildOf(audioRoot) || authoredSources[i].gameObject.name == "Lin voice")
                    ? -1f : authoredSources[i].volume;
            started = true;
            ResetTracking();
            StartScore();
        }

        void StartScore()
        {
            // Both layers share bar boundaries, so changing intensity never restarts a tune.
            double start = AudioSettings.dspTime + .08;
            music.PlayScheduled(start);
            tension.PlayScheduled(start);
            room.PlayScheduled(start);
        }

        // Confirm actual PCM reaches the running player, not merely an assigned clip.
        // Kept in the log for classroom setup; never displayed over the story.
        void CheckMusicOutput()
        {
            if(outputReported) return;
            if(++outputChecks==3) Debug.Log("[Audio] Playback state: playing="+music.isPlaying+"; position="+music.time+"; dsp="+AudioSettings.dspTime+"; gain="+music.volume+"; listener="+AudioListener.volume+"; paused="+AudioListener.pause+"; virtual="+music.isVirtual+"; focused="+Application.isFocused+"; loaded="+music.clip.loadState);
            if(music.volume < .05f || music.time < .5f) return;
            music.GetOutputData(outputProbe,0);
            double energy=0; foreach(float sample in outputProbe) energy+=sample*sample;
            float rms=(float)Math.Sqrt(energy/outputProbe.Length);
            if(rms < .001f) return;
            outputReported=true;
            Debug.Log("[Audio] Music PCM active: " + music.clip.name + "; RMS=" + rms.ToString("0.000") + "; gain=" + music.volume.ToString("0.00"));
        }

        void ResetTracking()
        {
            run = scene.State.RunId;
            playerPosition = scene.World.Player.position;
            linPosition = scene.World.Lin.position;
            playerStride = linStride = playerCooldown = linCooldown = 0f;
            evidenceMask = EvidenceMask();
            previousEnding = scene.State.Ending;
            wasDialogue = scene.DialogueOpen;
            wasJournal = scene.JournalOpen;
            wasIntro = scene.IntroOpen;
            wasModelWait = scene.ModelWait;
            transitionCooldown = 0f;
        }

        int EvidenceMask() => (scene.State.E1Verified ? 1 : 0) |
            (scene.State.E4Verified ? 2 : 0) | (scene.State.E5Verified ? 4 : 0);

        void LateUpdate()
        {
            if (!started || !scene || scene.State == null || !scene.World) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (rebuildPending) RecoverAudio();
            healthCheck-=dt;
            if(healthCheck<=0) { healthCheck=1f; if(!music.isPlaying || !room.isPlaying) RecoverAudio(); else CheckMusicOutput(); }
            cueCooldown = Mathf.Max(0f, cueCooldown - dt);
            transitionCooldown = Mathf.Max(0f, transitionCooldown - dt);
            if (run != scene.State.RunId) ResetTracking();

            bool isEnding = scene.State.IsTerminal;
            float progress = Mathf.Clamp01((float)(scene.State.ElapsedGameSeconds / SessionState.DeadlineSeconds));
            int mask = EvidenceMask();
            float unease = Mathf.Clamp01(progress * .65f + (mask != 0 ? .18f : 0f));
            if (scene.State.EscapeAgreed) unease *= .28f;
            float duck = GetComponent<LinVoice>() && GetComponent<LinVoice>().Speaking ? .42f : scene.DialogueOpen || scene.ModelWait || scene.JournalOpen ? .82f :
                scene.MenuOpen ? .75f : scene.IntroOpen ? 1f : 1f;
            if(Time.unscaledTime<previewUntil) duck=1f;
            float gain = muted ? 0f : masterVolume;
            mix = Mathf.MoveTowards(mix, gain * duck, dt * .65f);
            tensionMix = Mathf.MoveTowards(tensionMix, isEnding ? 0f : unease, dt * .18f);
            music.volume = mix * musicVolume * (isEnding ? .25f : 1.0f);
            tension.volume = mix * musicVolume * tensionMix * .7f;
            room.volume = mix * (isEnding ? .03f : .16f);
            foley.volume = gain * .55f;
            playerFeet.volume = gain * .7f;
            linFeet.volume = gain * .65f;
            ScaleAuthoredSounds(gain);

            if (mask != evidenceMask)
            {
                if ((mask & evidenceMask) == evidenceMask) PlayCue("confirm");
                evidenceMask = mask;
            }
            if (scene.DialogueOpen && !wasDialogue) PlayCue("dialogue");
            if (scene.JournalOpen && !wasJournal) PlayCue("paper");
            if (!scene.IntroOpen && wasIntro) PlayCue("paper");
            if (!scene.ModelWait && wasModelWait && scene.DialogueOpen) PlayCue("interact");
            wasDialogue = scene.DialogueOpen;
            wasJournal = scene.JournalOpen;
            wasIntro = scene.IntroOpen;
            wasModelWait = scene.ModelWait;
            string ending = scene.State.Ending;
            if (ending != previousEnding)
            {
                if (ending == "together") PlayCue("ending");
                else if (ending != "none") PlayCue("depart");
                previousEnding = ending;
            }

            Vector3 nextPlayer = scene.World.Player.position;
            Vector3 nextLin = scene.World.Lin.position;
            linFeet.transform.position = nextLin + Vector3.up * .15f;
            if (!scene.State.IsPaused)
            {
                StepFeet(nextPlayer, playerPosition, ref playerStride, ref playerCooldown,
                    playerFeet, ref stepIndex, .78f, dt);
                StepFeet(nextLin, linPosition, ref linStride, ref linCooldown,
                    linFeet, ref linStepIndex, .70f, dt);
            }
            else
            {
                playerStride = linStride = 0f;
                playerCooldown = linCooldown = 0f;
            }
            playerPosition = nextPlayer;
            linPosition = nextLin;
        }

        void ScaleAuthoredSounds(float gain)
        {
            for (int i = 0; i < authoredSources.Length; i++)
                if (authoredVolumes[i] >= 0f && authoredSources[i])
                    authoredSources[i].volume = authoredVolumes[i] * gain;
        }

        void StepFeet(Vector3 next, Vector3 previous, ref float stride, ref float cooldown,
            AudioSource source, ref int index, float strideLength, float dt)
        {
            cooldown = Mathf.Max(0f, cooldown - dt);
            Vector3 delta = next - previous;
            delta.y = 0f;
            float distance = delta.magnitude;
            // Restarts/teleports and pushing against a wall do not produce walking sounds.
            if (distance > 1f || distance < .001f) { stride = 0f; return; }
            stride += distance;
            if (stride < strideLength || cooldown > 0f) return;
            stride %= strideLength;
            cooldown = .2f;
            source.pitch = (index & 1) == 0 ? .96f : 1.04f;
            source.PlayOneShot(footsteps[index & 3]);
            index++;
        }

        /// <summary>Optional interaction hooks. Audio never writes gameplay state.</summary>
        public void PlayCue(string cue)
        {
            if (!started || muted || !isActiveAndEnabled) return;
            bool important = cue == "confirm" || cue == "ending" || cue == "depart";
            if (important ? transitionCooldown > 0f : cueCooldown > 0f) return;
            AudioClip clip;
            float volume = .65f;
            switch (cue)
            {
                case "paper": clip = paper; volume = .62f; break;
                case "confirm": clip = confirmed; volume = .5f; break;
                case "error": clip = error; volume = .38f; break;
                case "dialogue": clip = greeting; volume = .28f; break;
                case "ending": clip = closing; volume = .6f; break;
                case "depart": clip = leaving; volume = .5f; break;
                default: clip = touch; volume = .42f; break;
            }
            foley.volume = masterVolume * .55f;
            foley.PlayOneShot(clip, volume);
            if (important) transitionCooldown = .65f;
            else cueCooldown = .12f;
        }

        void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged += AudioChanged;
            if (started) { ResetTracking(); StartScore(); }
        }

        void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= AudioChanged;
            if (music) music.Stop();
            if (tension) tension.Stop();
            if (room) room.Stop();
            if (foley) foley.Stop();
            if (playerFeet) playerFeet.Stop();
            if (linFeet) linFeet.Stop();
            if (authoredSources != null) ScaleAuthoredSounds(1f);
        }

        void OnDestroy()
        {
            if (ownedClips != null)
                for (int i = 0; i < ownedClips.Length; i++)
                    if (ownedClips[i]) Destroy(ownedClips[i]);
            if (audioRoot) Destroy(audioRoot.gameObject);
        }

        static float Frequency(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        static float SoftKey(float time, float frequency, float duration)
        {
            if (time < 0f || time >= duration) return 0f;
            float attack = 1f - Mathf.Exp(-time * 75f);
            float end = Mathf.Clamp01((duration - time) / .18f);
            float phase = 2f * Mathf.PI * frequency * time;
            float body = Mathf.Sin(phase) + .24f * Mathf.Sin(phase * 2.001f) * Mathf.Exp(-time * 3f)
                + .07f * Mathf.Sin(phase * 3.997f) * Mathf.Exp(-time * 7f);
            return body * attack * Mathf.Exp(-time * 1.65f) * end;
        }

        static AudioClip Score(bool uneasy)
        {
            int frames = Mathf.RoundToInt(SampleRate * ScoreSeconds);
            var data = new float[frames * 2];
            // Eight bars at 80 BPM. D minor add9 -> Bbmaj7 -> Fadd9 -> Asus4.
            // Sparse felt-key voicings leave room for reading and conversation.
            int[,] chords = { { 50, 57, 64, 65 }, { 46, 53, 57, 62 },
                { 48, 53, 60, 67 }, { 45, 52, 57, 62 } };
            for (int bar = 0; bar < 8; bar++)
            {
                int chord = (bar / 2) % 4;
                if (uneasy)
                {
                    for (int note = 0; note < 4; note++)
                        AddNote(data, bar * BarSeconds + note * .75f + .16f,
                            chords[chord, note % 2] + 24, .95f, .055f, (note & 1) == 0 ? -.4f : .4f);
                }
                else
                {
                    AddNote(data, bar * BarSeconds, chords[chord, 0], 3.6f, .15f, -.2f);
                    AddNote(data, bar * BarSeconds + .18f, chords[chord, 2], 2.6f, .12f, .18f);
                    AddNote(data, bar * BarSeconds + 1.5f, chords[chord, 3] + 12, 2.3f, .105f, -.28f);
                    if ((bar & 1) == 1)
                        AddNote(data, bar * BarSeconds + 2.25f, chords[chord, 1] + 12, 2.3f, .08f, .25f);
                }
            }
            // A low, slowly breathing pad. Integer cycle counts make the loop seamless.
            for (int i = 0; i < frames; i++)
            {
                float t = i / (float)SampleRate;
                float phase = 2f * Mathf.PI * t / ScoreSeconds;
                float breath = .75f + .25f * Mathf.Sin(phase * 2f);
                float pad = (Mathf.Sin(phase * 1569f) + .30f * Mathf.Sin(phase * 2352f))
                    * breath * (uneasy ? .008f : .025f);
                data[i * 2] += pad;
                data[i * 2 + 1] += pad;
            }
            return Clip(uneasy ? "Original — Unanswered" : "Original — The Last Letter", data, 2);
        }

        static void AddNote(float[] data, float start, int midi, float duration, float amplitude, float pan)
        {
            int frames = data.Length / 2;
            int first = Mathf.RoundToInt(start * SampleRate);
            int count = Mathf.RoundToInt(duration * SampleRate);
            float frequency = Frequency(midi);
            float left = Mathf.Sqrt((1f - pan) * .5f);
            float right = Mathf.Sqrt((1f + pan) * .5f);
            for (int i = 0; i < count; i++)
            {
                float note = SoftKey(i / (float)SampleRate, frequency, duration) * amplitude;
                int frame = (first + i) % frames;
                data[frame * 2] += note * left;
                data[frame * 2 + 1] += note * right;
                // Baked soft room reflection; no scene reverb component or runtime DSP needed.
                int echo = (frame + 2867) % frames;
                data[echo * 2] += note * right * .14f;
                data[echo * 2 + 1] += note * left * .14f;
            }
        }

        static AudioClip RoomTone()
        {
            const int seconds = 12;
            int frames = SampleRate * seconds;
            var data = new float[frames];
            var noise = new Noise(0x25c18u);
            float filtered = 0f;
            for (int i = 0; i < frames; i++)
            {
                float t = i / (float)SampleRate;
                filtered += (noise.Next() - filtered) * .015f;
                float breathing = .65f + .35f * Mathf.Sin(2f * Mathf.PI * t / seconds);
                // Fade only the noise at the seam; the quiet hum is exactly periodic.
                float seam = Mathf.Clamp01(Mathf.Min(t, seconds - t) * 4f);
                seam = seam * seam * (3f - 2f * seam);
                data[i] = filtered * .12f * breathing * seam + Mathf.Sin(2f * Mathf.PI * 50f * t) * .006f;
            }
            return Clip("Original — Apartment air", data, 1);
        }

        static AudioClip Footstep(int variation)
        {
            float duration = .19f + variation * .009f;
            var data = new float[Mathf.RoundToInt(duration * SampleRate)];
            var noise = new Noise((uint)(4001 + variation * 271));
            float low = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                low += (noise.Next() - low) * .18f;
                float heel = Mathf.Sin(2f * Mathf.PI * (105f - variation * 4f) * t) * Mathf.Exp(-t * 45f);
                float sole = low * Mathf.Exp(-t * 25f);
                float toeTime = Mathf.Max(0f, t - .045f);
                float toe = t > .045f ? Mathf.Sin(2f * Mathf.PI * 155f * toeTime) * Mathf.Exp(-toeTime * 55f) : 0f;
                float attack = Mathf.Clamp01(t * 800f);
                data[i] = (heel * .20f + sole * .12f + toe * .055f) * attack
                    * Mathf.Clamp01((duration - t) / .035f);
            }
            return Clip("Original — Soft step " + variation, data, 1);
        }

        static AudioClip Sound(string name, float duration, int kind)
        {
            var data = new float[Mathf.RoundToInt(duration * SampleRate)];
            var noise = new Noise((uint)(8279 + kind * 199));
            float last = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float value;
                if (kind == 0)
                {
                    float n = noise.Next();
                    float high = n - last;
                    last += (n - last) * .5f;
                    float wave = Mathf.Pow(Mathf.Sin(Mathf.PI * t / duration), 2f);
                    float folds = .4f + .6f * Mathf.Pow(Mathf.Sin(2f * Mathf.PI * 8f * t), 2f);
                    value = high * wave * folds * .14f;
                }
                else if (kind == 1)
                    value = Mathf.Sin(2f * Mathf.PI * 420f * t) * Mathf.Exp(-t * 65f) * .22f;
                else if (kind == 2)
                    value = SoftKey(t, 196f, duration) * .18f;
                else if (kind == 3)
                    value = SoftKey(t, Frequency(69), duration) * .15f;
                else if (kind == 4)
                    value = SoftKey(t, Frequency(62), duration) * .15f
                        + SoftKey(t - .14f, Frequency(69), duration - .14f) * .12f
                        + SoftKey(t - .29f, Frequency(76), duration - .29f) * .09f;
                else if (kind == 5)
                    value = SoftKey(t, Frequency(50), duration) * .19f
                        + SoftKey(t - .24f, Frequency(57), duration - .24f) * .14f
                        + SoftKey(t - .48f, Frequency(64), duration - .48f) * .13f
                        + SoftKey(t - .9f, Frequency(69), duration - .9f) * .10f;
                else
                    value = SoftKey(t, Frequency(50), duration) * .17f
                        + SoftKey(t - .35f, Frequency(57), duration - .35f) * .12f
                        + SoftKey(t - .82f, Frequency(65), duration - .82f) * .09f;
                data[i] = value * Mathf.Clamp01(t * 700f) * Mathf.Clamp01((duration - t) / .025f);
            }
            return Clip(name, data, 1);
        }

        static AudioClip Clip(string name, float[] data, int channels)
        {
            if(!name.Contains("Apartment air") && !name.Contains("Unanswered"))
            {
                double power=0;float peak=0;
                foreach(float sample in data){power+=sample*sample;peak=Mathf.Max(peak,Mathf.Abs(sample));}
                float rms=(float)Math.Sqrt(power/Math.Max(1,data.Length));
                float level=name.Contains("step")?.17f:name.Contains("Last Letter")?.16f:.19f;
                float scale=Mathf.Min(level/Mathf.Max(.0001f,rms),.82f/Mathf.Max(.0001f,peak));
                for(int i=0;i<data.Length;i++)data[i]*=scale;
            }
            // A hard ceiling protects against accidental gain changes to layered notes.
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -.8f, .8f);
            var clip = AudioClip.Create(name, data.Length / channels, channels, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        struct Noise
        {
            uint state;
            public Noise(uint seed) { state = seed; }
            public float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0xffffff) / 8388607.5f - 1f;
            }
        }
    }
}
