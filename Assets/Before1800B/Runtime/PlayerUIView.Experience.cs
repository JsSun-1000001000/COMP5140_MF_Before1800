using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Before1800.PartB
{
    public sealed partial class PlayerUIView
    {
        RectTransform settingsLayer,settingsCard;
        Control settingsButton,languageButton,closeSettings,voiceToggle,testVoice,testMusic,testEffects;
        Text settingsTitle,settingsCaption,edition;
        Slider masterSlider,musicSlider,voiceSlider;
        Text masterLabel,musicLabel,voiceLabel;
        int previousIntro=-1;
        public bool SettingsOpen=>settingsLayer && settingsLayer.gameObject.activeSelf;
        public void OpenSettings()
        {
            Active(settingsLayer,true);
            var audio=scene.GetComponent<ResidenceAudio>();
            masterSlider.SetValueWithoutNotify(audio.Muted?0:audio.MasterVolume);
            musicSlider.SetValueWithoutNotify(audio.MusicVolume);
            voiceSlider.SetValueWithoutNotify(scene.GetComponent<LinVoice>().Volume);
            if(EventSystem.current) EventSystem.current.SetSelectedGameObject(languageButton.Node.gameObject);
        }
        public void CloseSettings(){Active(settingsLayer,false);scene.GetComponent<LinVoice>()?.Stop();if(EventSystem.current)EventSystem.current.SetSelectedGameObject(settingsButton.Node.gameObject);}
        void BuildExperienceControls()
        {
            edition=Label(intro,"Visit edition","最后一次来访  /  一段关于真相与选择的故事",14,Apricot);
            settingsButton=MakeButton(canvasRect,"SoundLanguage","声音与语言",OpenSettings);
            settingsLayer=FillNode(canvasRect,"Sound and language settings");
            var shade=Panel(settingsLayer,"Settings shade",new Color32(17,24,27,245));Fill(shade);shade.GetComponent<Image>().raycastTarget=true;
            settingsCard=Panel(settingsLayer,"Settings card",new Color32(30,40,42,255));
            settingsTitle=Label(settingsCard,"Settings title","让这间屋子有声音",32,Cream);
            settingsCaption=Label(settingsCard,"Settings caption","音乐、脚步与交谈，都可以调到舒服的音量。",15,Soft);
            languageButton=MakeButton(settingsCard,"Language","中文  /  English",()=>{GameLanguage.Change(GameLanguage.Code=="zh"?"en":"zh");previousIntro=-1;});
            masterLabel=Label(settingsCard,"Master label","总音量",16,Cream);masterSlider=MakeSlider(settingsCard,"Master",v=>{var a=scene.GetComponent<ResidenceAudio>();a.Muted=false;a.MasterVolume=v;});
            musicLabel=Label(settingsCard,"Music label","背景音乐",16,Cream);musicSlider=MakeSlider(settingsCard,"Music",v=>scene.GetComponent<ResidenceAudio>().MusicVolume=v);
            voiceLabel=Label(settingsCard,"Voice label","林的声音",16,Cream);voiceSlider=MakeSlider(settingsCard,"Voice",v=>scene.GetComponent<LinVoice>().Volume=v);
            voiceToggle=MakeButton(settingsCard,"Voice switch","角色语音：开",()=>{var v=scene.GetComponent<LinVoice>();v.Enabled=!v.Enabled;});
            testMusic=MakeButton(settingsCard,"Preview music","试听音乐",()=>scene.GetComponent<ResidenceAudio>()?.PreviewMusic());
            testEffects=MakeButton(settingsCard,"Preview effects","试听音效",()=>scene.GetComponent<ResidenceAudio>()?.PreviewEffects());
            testVoice=MakeButton(settingsCard,"Preview audio","试听人声",()=>{scene.GetComponent<ResidenceAudio>()?.PlayCue("interact");scene.GetComponent<LinVoice>()?.Preview(GameLanguage.T("我在这里。你听得见我吗？"));});
            closeSettings=MakeButton(settingsCard,"Close settings","完成   Esc",CloseSettings,true);
            Active(settingsLayer,false);
        }
        Slider MakeSlider(RectTransform parent,string name,UnityEngine.Events.UnityAction<float> changed)
        {
            var root=Node(parent,name);var slider=root.gameObject.AddComponent<Slider>();
            var track=Panel(root,"Track",new Color32(77,88,87,255));Fill(track,0,10);track.GetComponent<Image>().raycastTarget=true;
            var fillArea=Node(root,"Fill area");Fill(fillArea,7,10);var fill=Panel(fillArea,"Fill",Apricot);Fill(fill);
            var handles=Node(root,"Handle area");Fill(handles,7,0);var handle=Panel(handles,"Handle",Cream);handle.sizeDelta=new Vector2(14,26);handle.GetComponent<Image>().raycastTarget=true;
            slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Image>();slider.minValue=0;slider.maxValue=1;slider.onValueChanged.AddListener(changed);return slider;
        }
        void UpdateExperienceControls()
        {
            if(!settingsLayer)return;
            bool available=flow.Screen==PlayerScreen.Intro || flow.Screen==PlayerScreen.Menu;
            Active(settingsButton.Node,available && !SettingsOpen);
            if(!available && SettingsOpen)CloseSettings();
            Set(voiceToggle.Label,scene.GetComponent<LinVoice>().Enabled?"角色语音：开":"角色语音：关");
            if(flow.Screen!=PlayerScreen.Intro)return;
            if(previousIntro==flow.IntroStep)return;
            previousIntro=flow.IntroStep;changedLayout=true;
            introTitle.fontSize=flow.IntroStep==0?52:36;
            Set(introPlace,flow.IntroStep==0?"一封信，一小时，一个还没写好的明天。":"最后一次送信  ·  "+flow.IntroStep+" / 3");
            if(flow.IntroStep==0)
            {
                Set(introTitle,"18:00");Set(introSynopsis,"18 点之前\n一封回信，让最熟悉的房间变得陌生。");
                Set(introGoal,"走进林的生活。听她说起旧物与往事，也决定你愿意告诉她多少。\n约 12 分钟探索；交谈与阅读时，时间会等你。");Set(start.Label,"开始这次拜访   Enter");
            }
            else if(flow.IntroStep==1)
            {
                Set(introTitle,"你是谁");Set(introSynopsis,"你是信使，也是林多年的朋友。\n她以为你替自己与远方父母送信。");
                Set(introGoal,"你知道的真相：这间住宅是一处节目片场。父母的扮演者已经离场。剧组用隐藏镜头偷看她写信，你照着内容冒充父母写回信。\n今天，是你最后一次扮演信使。");Set(start.Label,"继续   Enter");
            }
            else if(flow.IntroStep==2)
            {
                Set(introTitle,"今天出了什么事");Set(introSynopsis,"林的原信还在茶几上，封口完好。\n你刚送来的回信，却提到了原信里才有的蓝杯子缺口。");
                Set(introGoal,"那个细节，她从没告诉过别人。\n她没有先怀疑整个世界；她先问眼前最信任的朋友：你怎么会知道？\n这一次，你准备怎样回答？");Set(start.Label,"继续   Enter");
            }
            else
            {
                Set(introTitle,"先听她说完");Set(introSynopsis,"你可以照常告别，也可以冒险帮助她调查。\n离不离开，最后仍由林决定。");
                Set(introGoal,"先跟随画面里的标记，走到茶几，对准两封信按 E。看清矛盾后，再和林说话。\n屋里的普通物品也有故事：对准沙发、书架、餐桌等按 E，听听她的回忆。\nWASD 移动 · 鼠标或方向键环顾 · J 手记 · Esc 暂停");Set(start.Label,"走进房间   Enter");
            }
        }
        void LayoutExperienceControls(float width,float height)
        {
            At(settingsButton.Node,width-194,24,162,40);At(edition.rectTransform,48,24,width-270,36);
            float w=Mathf.Min(540,width-40),h=496;
            At(settingsCard,(width-w)/2,(height-h)/2,w,h);
            At(settingsTitle.rectTransform,30,24,w-60,46);At(settingsCaption.rectTransform,30,72,w-60,42);
            At(languageButton.Node,30,123,w-60,40);
            var labels=new[]{masterLabel,musicLabel,voiceLabel};var sliders=new[]{masterSlider,musicSlider,voiceSlider};
            for(int i=0;i<3;i++){At(labels[i].rectTransform,30,181+i*47,120,32);At(sliders[i].GetComponent<RectTransform>(),164,181+i*47,w-194,32);}
            At(voiceToggle.Node,30,330,w-60,38);
            float buttonWidth=(w-80)/3;At(testMusic.Node,30,378,buttonWidth,38);At(testEffects.Node,40+buttonWidth,378,buttonWidth,38);At(testVoice.Node,50+buttonWidth*2,378,buttonWidth,38);
            At(closeSettings.Node,30,436,w-60,38);
        }
    }
}
