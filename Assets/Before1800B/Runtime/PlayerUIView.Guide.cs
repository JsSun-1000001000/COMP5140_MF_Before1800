using UnityEngine;
using UnityEngine.UI;

namespace Before1800.PartB
{
    public sealed partial class PlayerUIView
    {
        RectTransform guidePlate, guideMarker, letterLayer, letterCard;
        RectTransform[] letterPapers=new RectTransform[2];
        Text guideText,markerText,letterTitle,letterWhy,letterRole;
        Text[] letterHeads=new Text[2],letterBodies=new Text[2];
        Control letterContinue,letterClose,helpButton;
        void BuildGuideControls()
        {
            guidePlate=Panel(explore,"Next step",new Color32(24,32,34,225));
            guideText=Label(guidePlate,"Current objective","",15,Cream,TextAnchor.UpperLeft);
            guideMarker=Panel(explore,"Where to look",new Color32(37,43,42,220));
            markerText=Label(guideMarker,"Target marker","",14,Apricot,TextAnchor.MiddleCenter);
            Fill(markerText.rectTransform,8,0);
            helpButton=MakeButton(top,"How to play","玩法   H",()=>scene.Story?.OpenHelp());
            letterLayer=FillNode(canvasRect,"Two letters");
            var shade=Panel(letterLayer,"Letter backdrop",new Color32(19,25,27,220));Fill(shade);shade.GetComponent<Image>().raycastTarget=true;
            letterCard=Panel(letterLayer,"Letter comparison",new Color32(34,42,43,255));
            letterTitle=Label(letterCard,"A contradiction","两封信，一处不可能的细节",28,Cream);
            for(int i=0;i<2;i++)
            {
                letterPapers[i]=Panel(letterCard,"Letter "+i,Paper);
                letterHeads[i]=Label(letterPapers[i],"From whom",i==0?"林写给父母 · 还没寄出":"你今天送来 · 冒充父母",17,new Color32(143,81,51,255));
                letterBodies[i]=Label(letterPapers[i],"Relevant sentence",i==0?"昨晚，我把蓝杯子的缺口转向墙。\n\n——林念出自己原信中的一句。":"把蓝杯子的缺口转向墙，并不等于它不存在。\n\n——署名：爸、妈",21,Ink,TextAnchor.UpperLeft);
            }
            letterWhy=Label(letterCard,"Why it matters","原信还封在家里，父母不可能收到。可回信已经在回答它。",19,Apricot);
            letterRole=Label(letterCard,"Your secret","你知道：剧组用隐藏镜头看见了她写信，你照着内容代写回信。\n林不知道。她先怀疑的，是替她送信多年的你。",17,Soft);
            letterContinue=MakeButton(letterCard,"Answer Lin","我明白了，回去回答林   Enter",()=>{flow.Choose("answer_letters");},true);
            letterClose=MakeButton(letterCard,"Keep looking","先回到房间",()=>flow.Back());
            Active(letterLayer,false);
        }
        void UpdateGuideControls()
        {
            if(!guidePlate || !scene.Story)return;
            bool comparison=scene.Story.LetterComparison;
            foreach(var body in letterBodies)body.fontSize=GameLanguage.Code=="en"?18:21;
            bool was=letterLayer.gameObject.activeSelf;
            Active(letterLayer,comparison);
            Active(modalPlate,!comparison);
            if(comparison && !was && UnityEngine.EventSystems.EventSystem.current)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(letterContinue.Node.gameObject);
            Active(helpButton.Node,flow.Screen==PlayerScreen.Explore || flow.Screen==PlayerScreen.Talk);
            bool exploring=flow.Screen==PlayerScreen.Explore;
            Active(guidePlate,exploring);Active(guideMarker,exploring);
            if(!exploring)return;
            string caption;Transform target;
            var state=scene.State;var story=scene.Story;var world=scene.World;
            if(!story.LettersCompared) {caption="1 / 6  看懂信件\n对准茶几上的信封，按 E。";target=story.PaperFocus("parent_reply");}
            else if(!story.FirstAnswerSent) {caption="2 / 6  回应林\n走近林按 E，说出你准备怎样解释。";target=world.Lin;}
            else if(!state.E1Verified) {caption="3 / 6  一起检查镜头\n对准电视上方的镜头按 E，邀请林过去。";target=world.CameraTarget;}
            else if(!state.E4Verified) {caption="4 / 6  试试电视\n对准电视按 E，先选预测，再一起验证。";target=world.Tv.Screen;}
            else if(!state.E5Verified) {caption=world.Station.DoorOpen?"5 / 6  请林看看墙后\n走近林按 E，邀请她检查打开的墙板。":"5 / 6  看穿这面墙\n走进前门门厅，抬头看台灯上方的黄铜卡扣，按 E 打开。";target=world.Station.DoorOpen?world.Lin:world.Station.DoorControl;}
            else if(!state.RouteConfirmed) {caption="5 / 6  确认出路\n走到前门金线，面向出口牌按 E。";target=world.Station.RouteSign;}
            else if(!state.EscapeAgreed) {caption=!state.IsConcernResolved("C1")?"6 / 6  把事实连起来\n告诉林：镜头、电视和墙后支架说明了什么。":!state.IsConcernResolved("C2")?"6 / 6  说清你的责任\n回答林关于代写回信的问题，别把责任推给剧组。":!state.IsConcernResolved("C3")?"6 / 6  谈谈门外\n承认未知，说清你愿意陪她走的第一步。":"6 / 6  把选择交还给她\n问林是否现在愿意同行，听她自己的决定。";target=world.Lin;}
            else {caption="6 / 6  一起告别\n等林跟到前门，面对出口牌按 E。";target=world.Station.RouteSign;}
            if(world.YieldingToPlayer)caption="让开一点位置\n往旁边走一步，林会继续过去。";
            else if(world.WaitingForClearSight)caption="让林看清楚\n别挡在她和物品之间，观察会继续。";
            else if(state.Stage==ActionStage.Moving)caption="跟着林走\n她正在过去。给她留出通路。";
            else if(state.Stage==ActionStage.Returning)caption="调查完成\n林正在回到客厅。你可以继续找下一处线索。";
            else if(state.Stage==ActionStage.Arrived && state.Active?.TargetId==SessionState.CameraTarget)
                caption="看镜头是否跟着转\n对准电视上方镜头按 E，林会换一个位置。";
            else if(state.Stage==ActionStage.Arrived && state.Active?.TargetId==SessionState.BroadcastTarget)
                caption=world.Tv.CanStartSequence?"电视 · 第一步\n对准屏幕按 E，启动刚才登记的预测。":world.Tv.CanConfirmPredictedShow?"电视 · 第二步\n再按 E，请林点播你预测的节目。":"电视 · 第三步\n再按 E，请她改口，观察画面是否改变。";
            else if(state.Stage==ActionStage.Arrived && state.Active?.TargetId==SessionState.SetBackTarget)
                caption="让她亲眼看看\n对准打开的墙板卡扣按 E，请林观察支架。";
            Set(guideText,caption);
            var size=canvasRect.rect.size;
            if(target)
            {
                var point=target==world.Lin?target.position+Vector3.up*1.7f:target.position;
                var v=world.ViewCamera.WorldToViewportPoint(point);
                bool behind=v.z<0;
                float x=(behind?1-v.x:v.x)*size.x,y=(1-v.y)*size.y-28;
                x=Mathf.Clamp(x,104,size.x-104);y=Mathf.Clamp(behind?size.y*.45f:y,185,size.y-170);
                At(guideMarker,x-96,y,192,31);
                string label=target==world.Lin?"林":!story.LettersCompared?"两封信":!state.E1Verified?"镜头":!state.E4Verified?"电视":!state.E5Verified?"墙板卡扣":"前门";
                markerText.text=(behind?GameLanguage.T("转身 · "):"◇ ")+GameLanguage.T(label)+"  "+Vector3.Distance(world.Player.position,point).ToString("0.0")+" m";
            }
        }
        void LayoutGuideControls(float width,float height)
        {
            At(guidePlate,32,76,Mathf.Min(370,width*.43f),90);At(guideText.rectTransform,13,10,guidePlate.sizeDelta.x-26,72);
            At(helpButton.Node,width-132,74,100,35);
            float w=Mathf.Min(790,width-48),h=476,x=(width-w)/2,y=(height-h)/2;
            At(letterCard,x,y,w,h);At(letterTitle.rectTransform,28,22,w-56,44);
            float pw=(w-68)/2;
            for(int i=0;i<2;i++){At(letterPapers[i],28+i*(pw+12),81,pw,190);At(letterHeads[i].rectTransform,18,12,pw-36,28);At(letterBodies[i].rectTransform,18,50,pw-36,138);}
            At(letterWhy.rectTransform,28,284,w-56,46);At(letterRole.rectTransform,28,336,w-56,64);
            At(letterContinue.Node,28,423,w-242,37);At(letterClose.Node,w-200,423,172,37);
        }
    }
}
