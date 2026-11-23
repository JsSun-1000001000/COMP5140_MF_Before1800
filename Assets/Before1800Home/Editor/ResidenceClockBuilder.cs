using Before1800.PartB;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Before1800.Home.Editor
{
    internal static class ResidenceClockBuilder
    {
        public static void Build(GameObject root, Transform anchors, Transform sourceClock, Transform sourceRemote, Transform sourceTV)
        {
            var clock = At("Kitchen wall clock", anchors, sourceClock.position);
            clock.rotation = sourceClock.rotation;
            Transform seconds = null;
            foreach (Transform original in sourceClock) {
                var hand = CopyProp(original, clock);
                if (original.name == "WallClocksBigArrow") seconds = hand;
            }
            if (!seconds) throw new System.InvalidOperationException("Kitchen clock has no original seconds hand.");
            // The face stays in the apartment mesh. Sight ends just in front of its glass.
            var face = At("Clock face", clock, clock.position - .023f * clock.up);
            var tick = clock.gameObject.AddComponent<AudioSource>();
            tick.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Before1800Home/Clock 1.ogg");
            tick.playOnAwake = false;
            tick.spatialBlend = 1f;
            tick.rolloffMode = AudioRolloffMode.Linear;
            tick.volume = .52f;
            tick.minDistance = .8f;
            tick.maxDistance = 6f;
            tick.dopplerLevel = 0f;

            var remote = CopyProp(sourceRemote, anchors);
            var remoteTop = remote.GetComponent<Renderer>().bounds;
            var buttons = At("Remote buttons", remote, remoteTop.center + Vector3.up * (remoteTop.extents.y + .012f));

            // The source TV's own world-space canvas sits on the screen, ahead of the case.
            var surface = sourceTV.Find("Canvas");
            var tvTarget = At("Living room TV screen", anchors, surface.position + Vector3.forward * .012f);
            var card = CueCard(tvTarget);
            root.AddComponent<ClockRehearsalProps>().Configure(face, buttons, tvTarget, seconds, remote.gameObject, card, tick);
        }

        static Transform At(string name, Transform parent, Vector3 position)
        {
            var item = new GameObject(name).transform;
            item.SetParent(parent, false);
            item.position = position;
            return item;
        }

        static Transform CopyProp(Transform original, Transform parent)
        {
            var item = At(original.name, parent, original.position);
            item.rotation = original.rotation;
            item.localScale = original.lossyScale;
            item.gameObject.AddComponent<MeshFilter>().sharedMesh = original.GetComponent<MeshFilter>().sharedMesh;
            item.gameObject.AddComponent<MeshRenderer>().sharedMaterials = original.GetComponent<MeshRenderer>().sharedMaterials;
            return item;
        }

        static GameObject CueCard(Transform target)
        {
            var card = new GameObject("TV rehearsal cue", typeof(RectTransform), typeof(Canvas));
            var rect = (RectTransform)card.transform;
            rect.SetParent(target, false);
            rect.localRotation = Quaternion.Euler(0, 180, 0);
            rect.localScale = Vector3.one * .002f;
            rect.sizeDelta = new Vector2(640, 360);
            card.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var screen = card.AddComponent<Image>();
            screen.color = new Color32(22, 33, 35, 255);
            screen.raycastTarget = false;
            var type = PlayerUIFont.Get();
            CueLine(rect, type, "重来", 89, 39, new Color32(244, 221, 169, 255));
            CueLine(rect, type, "从上一句接", 43, -66, new Color32(236, 238, 229, 255));
            return card;
        }

        static void CueLine(RectTransform frame, Font font, string line, int size, float y, Color color)
        {
            var label = new GameObject(line, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.rectTransform.SetParent(frame, false);
            label.rectTransform.sizeDelta = new Vector2(frame.sizeDelta.x, size * 1.7f);
            label.rectTransform.anchoredPosition = Vector2.up * y;
            label.font = font;
            label.fontSize = size;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.raycastTarget = false;
            label.text = line;
        }
    }
}
