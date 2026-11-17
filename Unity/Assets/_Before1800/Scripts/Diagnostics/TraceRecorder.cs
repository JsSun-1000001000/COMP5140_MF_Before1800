using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Before1800
{
    public sealed class TraceRecorder
    {
        private readonly Queue<string> lines = new Queue<string>();
        public string FilePath { get; }
        public string RecentText => string.Join("\n", lines);
        public TraceRecorder()
        {
            string directory = Path.Combine(Application.persistentDataPath, "traces");
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".jsonl");
        }
        public void Record(string kind, object data, string summary)
        {
            var record = new { utc = DateTime.UtcNow.ToString("O"), kind, data };
            try { File.AppendAllText(FilePath, JsonConvert.SerializeObject(record) + Environment.NewLine); }
            catch (IOException error) { Debug.LogWarning("Trace write failed: " + error.Message); }
            lines.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + kind + "  " + summary);
            while (lines.Count > 18) lines.Dequeue();
        }
    }
}
