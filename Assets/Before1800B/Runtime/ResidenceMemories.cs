using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Before1800.PartB
{
    public sealed class ResidenceMemories : MonoBehaviour
    {
        public sealed class Place { public string Id,Name,Memory; public Transform Prop,Focus; }
        readonly List<Place> places=new List<Place>();
        readonly HashSet<string> noticed=new HashSet<string>();
        PartBScene scene; string dwell,queued;float dwellTime,inspection;Guid run;ActionTicket ticket;
        public static readonly string[] Ids={"memory_sofa","memory_books","memory_plant","memory_table","memory_shoes","memory_lamp","memory_mirror","memory_fridge","memory_chair","memory_window"};
        public static bool IsTarget(string id)=>Array.IndexOf(Ids,id)>=0;
        public IEnumerable<Place> Places=>places;
        public string FocusName=>Focused()?.Name;
        public string Hint=>Focused() is Place p ? "聊聊 · "+p.Name : "";
        public string[] Knowledge { get {var list=new List<string>();foreach(var p in places)list.Add(p.Id+"（"+p.Name+"）："+p.Memory);return list.ToArray();} }
        public void Bind(PartBScene owner)
        {
            scene=owner; run=scene.State.RunId;
            Add(0,"Sofa","旧沙发","十六岁那年暴雨停电，你和信使坐在地毯上各讲一个很烂的笑话，谁先笑谁洗杯子。你先笑，却赖他说话时鼻子在抖。沙发左边凹下去的位置原来是父亲的；你独处时偏要坐右边。你仍记得开心，不把每段快乐都解释成阴谋。");
            Add(1,"LivingRoomBookScaff","书架","你喜欢旅行随笔，在页边纠正作者的夸张比喻。十九岁想报名图书修复课程，一封父母回信说外面太乱、晚一年也不迟，你便留下来，后来一直没再报。你曾把一本海边游记借给信使，书里夹着你画的小船；不预设他是否发现。想开一个能修书也能喝茶的小店。");
            Add(2,"Plant 6","窗边的植物","阿禾送你一株植物，说不要每天拔出来看它有没有长根。你因为太着急浇死过一盆，后来学会把浇水日画在日历上。你会给这一盆取很郑重的名字『顾先生』，却不愿别人当众叫出来。这是能开玩笑的日常，不是证据。");
            Add(3,"Kitchen Table","餐桌","母亲总把面团揉得太硬，父亲假装牙很好，三个人最后笑得吃不下。你喜欢葱油面，讨厌别人替你挑掉葱。每周五习惯多摆一双筷子，后来会默默收起来。你想学会一道由自己决定味道的菜，而不是永远复刻别人。");
            Add(4,"Shoes","门边的鞋","阿禾曾邀你去看海，你换好鞋又回屋取一封父母的信，回来时末班车已错过。你对她说是鞋带断了。她没有揭穿，只教你打一个更牢的结。你会承认自己也用小谎保护过自尊，但不会用这件事免除信使的欺骗。");
            Add(5,"BigLamp","落地灯","父亲有次修灯，螺丝滚到沙发下面，你拿勺子去捞，他说这叫勺子工程。你后来独自换过灯泡，有一点得意。你习惯只亮这盏灯读信，因为大灯让房间像等待检查。没有证据时不知道灯具属于片场。");
            Add(6,"Bedroom Mirror","卧室镜子","你曾对着镜子练习申请修书课程的自我介绍，一紧张就把名字说两遍。信使敲门时你装作在唱歌。你不喜欢别人说你『乖』，更想被说有主意。卧室是私人空间，没邀请不要翻动抽屉；可以在门口谈这段经历。");
            Add(7,"Fridge","冰箱","你在冰箱上贴过自制的『出去看看』清单，第一项是买一颗不知道名字的水果。后来父母的信说等他们回来一起去，你把清单收起来了。你并非不想走，而是把等待当成维系关系的方法。现在仍会纠结是不是只有离开才算勇敢。");
            Add(8,"LivingRoomBook Chair","阅读椅","阿禾最爱倒坐这把椅子，给每个路过的人编职业，唯独说不出你以后会做什么。你当时很生气，现在觉得她是在等你自己说。你会分辨别人好意里的冒犯；如果玩家只把你当需要拯救的人，会请他先听你讲完。");
            Add(9,"Kitchen TumbWindow","厨房窗边","你每天看窗边一小块光走到桌角，凭这个决定泡第二杯茶。你喜欢雨前的土味，却不喜欢所有人都说雨天必须忧伤。阿禾说过窗外也有很普通、很无聊的日子，你反而觉得那很诱人。未经亲见，不把窗外景色直接说成假景。");
        }
        void Add(int index,string objectName,string title,string memory)
        {
            Transform prop=null;
            foreach(var t in FindObjectsOfType<Transform>())if(t.name==objectName){prop=t;break;}
            if(!prop)return;
            Bounds bounds=new Bounds(prop.position,Vector3.one*.2f);bool first=true;
            foreach(var renderer in prop.GetComponentsInChildren<Renderer>()) { if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds); }
            var focus=new GameObject(Ids[index]+" focus").transform;focus.SetParent(transform);focus.position=bounds.center;
            Vector3 best=prop.position;float cost=float.PositiveInfinity;
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8;var p=bounds.center+new Vector3(Mathf.Cos(a)*(bounds.extents.x+.85f),0,Mathf.Sin(a)*(bounds.extents.z+.85f));p.y=.02f;
                if(NavMesh.SamplePosition(p,out var hit,.65f,NavMesh.AllAreas))
                { float d=Vector3.Distance(hit.position,scene.World.Lin.position);if(d<cost){cost=d;best=hit.position;} }
            }
            if(float.IsInfinity(cost)){Destroy(focus.gameObject);return;}
            var goal=new GameObject(Ids[index]+" approach").transform;goal.SetParent(transform);goal.position=best;
            scene.World.RegisterStoryTarget(Ids[index],goal,focus);
            places.Add(new Place {Id=Ids[index],Name=title,Memory=memory,Prop=prop,Focus=focus});
        }
        Place Focused()
        {
            if(!scene || scene.State==null)return null;
            var view=scene.World.ViewCamera.transform;Place best=null;float score=22;
            foreach(var p in places)
            {var delta=p.Focus.position-view.position;float angle=Vector3.Angle(view.forward,delta);
             if(delta.magnitude>3.8f || angle>score)continue;
             if(!InteractionSight.Clear(view.position,delta,scene.World.Player,p.Prop))continue;
             best=p;score=angle;}
            return best;
        }
        public bool TalkAbout()
        {
            var p=Focused();if(p==null)return false;
            if(Vector3.Distance(scene.World.Player.position,scene.World.Lin.position)>5)return false;
            if(!scene.DialogueOpen)scene.ToggleDialogue();
            var flow=scene.GetComponent<GreyboxHUD>().Flow;
            flow.Send(GameLanguage.T("看着这件东西，我想问你：")+GameLanguage.T(p.Name)+GameLanguage.T("让你想起了什么？"));return true;
        }
        public string Observation=>Focused() is Place p ? "玩家当前面向"+p.Name+"，可以聊生活，不必转回信件或调查。" : "";
        public string Consume(){var q=queued;queued=null;return q;}
        void Update()
        {
            if(!scene)return;
            if(run!=scene.State.RunId){run=scene.State.RunId;noticed.Clear();queued=dwell=null;dwellTime=inspection=0;ticket=null;}
            if(scene.State.IsPaused)return;
            var active=scene.State.Active;
            if(active!=null && IsTarget(active.TargetId) && scene.State.Stage==ActionStage.Arrived)
            {
                if(ticket!=active){ticket=active;inspection=0;}inspection+=Time.deltaTime;
                if(inspection>=1.4f && scene.World.AtObservation(active.TargetId))
                {
                    var p=places.Find(x=>x.Id==active.TargetId);
                    if(scene.State.TryCompleteStoryInspection(active)){scene.World.ResetTravel();queued="你已亲自走到"+p.Name+"旁，可以从自己的生活回忆谈起；这不是新的案件证据。";}
                }
            }
            var focus=Focused();
            if(focus==null || Vector3.Distance(scene.World.Player.position,scene.World.Lin.position)>4.5f){dwellTime=0;dwell=null;return;}
            var eye=scene.World.Lin.position+Vector3.up*1.5f;
            if(!InteractionSight.Clear(eye,scene.World.Player.position+Vector3.up-eye,scene.World.Lin,scene.World.Player))return;
            if(dwell!=focus.Id){dwell=focus.Id;dwellTime=0;}
            dwellTime+=Time.deltaTime;
            if(dwellTime>8 && noticed.Add(focus.Id))queued="你看见朋友在"+focus.Name+"前静静停了一会儿。可以谈起与它有关的一段回忆，也可以轻轻问朋友在想什么。不要重复逼问信件。";
        }
    }
}
