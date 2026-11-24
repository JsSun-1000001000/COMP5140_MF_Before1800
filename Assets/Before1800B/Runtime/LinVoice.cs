using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Before1800.PartB
{
    [DefaultExecutionOrder(500)]
    public sealed class LinVoice : MonoBehaviour
    {
        [Serializable] class Speech { public string text, language; }
        PartBScene scene;
        AudioSource source;
        UnityWebRequest activeRequest; Coroutine synthesis;
        SkinnedMeshRenderer face;
        int mouth = -1, generation;
        float mouthWeight;
        float[] samples = new float[256];
        string previous = "", previousLanguage;
        Guid run; bool preview, rendering;
        internal bool Previewing => preview;
        public bool Speaking => source && source.isPlaying;
        public bool Available { get; private set; } = true;
        public float MouthWeight => mouthWeight;
        public float Volume { get => PlayerPrefs.GetFloat("Before1800.Voice", .9f); set => PlayerPrefs.SetFloat("Before1800.Voice", Mathf.Clamp01(value)); }
        public bool Enabled { get => PlayerPrefs.GetInt("Before1800.Voice.On",1) != 0; set { PlayerPrefs.SetInt("Before1800.Voice.On",value?1:0); if(!value) Stop(); } }
        void Start()
        {
            scene=GetComponent<PartBScene>();
            var go=new GameObject("Lin voice"); go.transform.SetParent(transform,false);
            source=go.AddComponent<AudioSource>(); source.playOnAwake=false; source.spatialBlend=0; source.ignoreListenerPause=true;
            foreach(var renderer in scene.World.Lin.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!renderer.sharedMesh) continue;
                string[] candidates={"MouthOpen","Mouth_Open","A","aa","vrc.v_aa","Talk","OpenMouth"};
                foreach(var name in candidates) { int index=renderer.sharedMesh.GetBlendShapeIndex(name); if(index>=0){face=renderer;mouth=index;break;} }
                if(mouth<0) for(int i=0;i<renderer.sharedMesh.blendShapeCount;i++)
                { string name=renderer.sharedMesh.GetBlendShapeName(i).ToLowerInvariant(); if(name.Contains("mouth") && name.Contains("open")){face=renderer;mouth=i;break;} }
                if(mouth>=0) break;
            }
            Debug.Log("[Voice] Mouth shape: " + (face ? face.sharedMesh.GetBlendShapeName(mouth) : "unavailable"));
        }
        void Update()
        {
            if(!scene || !source) return;
            if(Application.isEditor && !GetComponent<PartCDialogue>().IsLive && !preview)return;
            if(run!=scene.State.RunId || previousLanguage!=GameLanguage.Code)
            { Stop(); previous="";run=scene.State.RunId;previousLanguage=GameLanguage.Code; }
            var flow=GetComponent<GreyboxHUD>().Flow;
            if(preview) { if(!rendering && !Speaking)preview=false;return; }
            if(!scene.DialogueOpen || scene.MenuOpen || scene.IntroOpen || scene.State.IsTerminal) { if(Speaking || activeRequest!=null) Stop(); return; }
            if(scene.ModelWait) { if(Speaking || activeRequest!=null) Stop(); return; }
            if(flow.Screen!=PlayerScreen.Talk) return;
            string line=GameLanguage.T(flow.Line);
            if(line!=previous && !string.IsNullOrWhiteSpace(line)) { previous=line; Speak(line); }
        }
        public void Preview(string line){Speak(line);preview=true;}
        public void Speak(string line)
        {
            Stop();
            if(!Enabled || !source) return;
            rendering=true;
            synthesis=StartCoroutine(Render(line,++generation));
        }
        IEnumerator Render(string text,int version)
        {
            string json=JsonUtility.ToJson(new Speech {text=text,language=GameLanguage.Code});
            try
            {
              using(var http=new UnityWebRequest("http://127.0.0.1:8180/speech","POST"))
              {
                activeRequest=http;
                http.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                http.downloadHandler=new DownloadHandlerBuffer();http.SetRequestHeader("Content-Type","application/json");http.timeout=30;
                var op=http.SendWebRequest();
                while(!op.isDone && version==generation) yield return null;
                if(version!=generation){yield break;}
                activeRequest=null;
                if(http.result!=UnityWebRequest.Result.Success){Available=false;yield break;}
                AudioClip clip=null;
                try { clip=Decode(http.downloadHandler.data); } catch(Exception e) { Debug.LogWarning("[Voice] Audio could not be decoded: "+e.GetType().Name); }
                Available=clip;
                if(!clip) yield break;
                if(source.clip) Destroy(source.clip);
                source.clip=clip;source.Play();
                Debug.Log("[Voice] Playing "+GameLanguage.Code+" "+clip.length.ToString("F1")+"s");
              }
            }
            finally { if(version==generation){activeRequest=null;rendering=false;synthesis=null;} }
        }
        public void Stop()
        {
            preview=false;generation++;
            // Abort while the request is still alive; stopping its coroutine disposes it.
            if(activeRequest!=null)activeRequest.Abort();
            if(synthesis!=null){StopCoroutine(synthesis);synthesis=null;}
            activeRequest=null;rendering=false;
            if(source)source.Stop();if(face && mouth>=0)face.SetBlendShapeWeight(mouth,0);mouthWeight=0;
        }
        void LateUpdate()
        {
            if(!source) return;
            var mix=GetComponent<ResidenceAudio>();
            source.volume=(mix && mix.Muted?0:mix?mix.MasterVolume:1)*Volume;
            float rms=0;
            if(Speaking){source.GetOutputData(samples,0);foreach(var sample in samples)rms+=sample*sample;rms=Mathf.Sqrt(rms/samples.Length);}
            float target=Speaking?Mathf.Clamp(rms*620,0,85):0;
            mouthWeight=Mathf.Lerp(mouthWeight,target,1-Mathf.Exp(-Time.unscaledDeltaTime*24));
            if(face && mouth>=0) face.SetBlendShapeWeight(mouth,mouthWeight);
        }
        internal static AudioClip Decode(byte[] bytes)
        {
            using(var reader=new BinaryReader(new MemoryStream(bytes)))
            {
                if(new string(reader.ReadChars(4))!="RIFF")return null;
                reader.ReadInt32();if(new string(reader.ReadChars(4))!="WAVE")return null;
                int channels=1,rate=22050,bits=16,format=1;byte[] pcm=null;
                while(reader.BaseStream.Position+8<=bytes.Length)
                {
                    string id=new string(reader.ReadChars(4));int size=reader.ReadInt32();long next=reader.BaseStream.Position+size+(size&1);
                    if(size<0 || next>bytes.Length) break;
                    if(id=="fmt "){format=reader.ReadUInt16();channels=reader.ReadUInt16();rate=reader.ReadInt32();reader.ReadInt32();reader.ReadUInt16();bits=reader.ReadUInt16();}
                    if(id=="data")pcm=reader.ReadBytes(size);
                    reader.BaseStream.Position=next;
                }
                if(pcm==null || format!=1 || bits!=16 || channels<1)return null;
                float[] data=new float[pcm.Length/2];for(int i=0;i<data.Length;i++)data[i]=(short)(pcm[i*2]|pcm[i*2+1]<<8)/32768f;
                var clip=AudioClip.Create("Lin spoken reply",data.Length/channels,channels,rate,false);clip.SetData(data,0);return clip;
            }
        }
        void OnDestroy(){Stop();if(source && source.clip)Destroy(source.clip);}
    }
}
