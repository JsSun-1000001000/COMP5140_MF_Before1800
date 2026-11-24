using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
namespace Before1800.PartB
{
    public static class GameLanguage
    {
        [Serializable] class Pair {public string zh,en;}
        [Serializable] class Catalog {public Pair[] entries;}
        static Dictionary<string,string> english;
        static List<Pair> fragments;
        public static string Code=>PlayerPrefs.GetString("Before1800.Language","zh");
        public static int Revision {get;private set;}
        public static void Change(string code){if(code!="zh" && code!="en")return;PlayerPrefs.SetString("Before1800.Language",code);Revision++;}
        public static string Clean(string s)
        {
            if(string.IsNullOrEmpty(s))return "";
            s=s.Replace("[E] ","").Replace("E4 已确认。", "手记已更新。").Replace("E1 已确认。", "手记已更新。").Replace("不计 E4", "本次尚未确认").Replace("E4 预测实验", "电视预测");
            s=Regex.Replace(s,@"\b[EC][1-5]\b[：:]?\s*","");
            return s;
        }
        public static string T(string text)
        {
            if(string.IsNullOrEmpty(text))return "";
            text=Clean(text);
            if(Code!="en")return text;
            if(english==null)
            {
                english=new Dictionary<string,string>();fragments=new List<Pair>();var asset=Resources.Load<TextAsset>("locale_en");
                if(asset){var catalog=JsonUtility.FromJson<Catalog>(asset.text);foreach(var pair in catalog.entries){english[Clean(pair.zh)]=pair.en;if(pair.zh.Length>=2)fragments.Add(new Pair{zh=Clean(pair.zh),en=pair.en});}}
                fragments.Sort((a,b)=>b.zh.Length.CompareTo(a.zh.Length));
            }
            if(english.TryGetValue(text,out var translated))return translated;
            foreach(var pair in fragments) if(text.Contains(pair.zh))text=text.Replace(pair.zh,pair.en);
            return text;
        }
    }
    public sealed class WorldLanguage:MonoBehaviour
    {
        sealed class Label {public TextMesh Mesh;public string Source,Shown;}
        readonly List<Label> labels=new List<Label>();
        float next;bool initialized;
        void LateUpdate()
        {
            if(Time.unscaledTime<next)return;next=Time.unscaledTime+.25f;
            if(!initialized){foreach(var text in FindObjectsOfType<TextMesh>(true))labels.Add(new Label{Mesh=text,Source=text.text});initialized=true;}
            foreach(var label in labels)
            {
                if(!label.Mesh)continue;
                if(label.Shown!=null && label.Mesh.text!=label.Shown)label.Source=label.Mesh.text;
                label.Shown=GameLanguage.T(label.Source);label.Mesh.text=label.Shown;
            }
        }
    }
}
