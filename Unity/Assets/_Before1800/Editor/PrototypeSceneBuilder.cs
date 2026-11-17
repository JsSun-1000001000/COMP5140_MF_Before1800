using System;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Before1800.Editor
{
    public static class PrototypeSceneBuilder
    {
        private const string ScenePath="Assets/_Before1800/Scenes/Prototype_E1.unity";
        private static string materials="Assets/_Before1800/Materials/";
        [MenuItem("Before 18/Create E1 prototype scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/_Before1800/Scenes");Directory.CreateDirectory(materials);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var walls=Material("Plaster",new Color(.56f,.59f,.54f));
            var floor=Material("WarmWood",new Color(.27f,.2f,.14f));
            var trim=Material("DarkTrim",new Color(.08f,.105f,.11f));
            var cream=Material("Cream",new Color(.78f,.72f,.58f));
            var teal=Material("MutedTeal",new Color(.17f,.36f,.34f));
            var black=Material("Lens",new Color(.015f,.028f,.035f),.7f);
            var glow=Material("Indicator",new Color(.37f,.95f,.72f),0,new Color(.25f,1f,.6f)*2);
            var environment=new GameObject("House - greybox environment");
            Cube("Floor",environment.transform,new Vector3(0,-.15f,0),new Vector3(12,.3f,10),floor);
            Cube("Back wall",environment.transform,new Vector3(0,1.8f,5),new Vector3(12,3.6f,.22f),walls);
            Cube("Front wall",environment.transform,new Vector3(0,1.8f,-5),new Vector3(12,3.6f,.22f),walls);
            Cube("West wall",environment.transform,new Vector3(-6,1.8f,0),new Vector3(.22f,3.6f,10),walls);
            Cube("East wall",environment.transform,new Vector3(6,1.8f,0),new Vector3(.22f,3.6f,10),walls);
            Cube("Ceiling",environment.transform,new Vector3(0,3.7f,0),new Vector3(12,.2f,10),cream);
            Cube("Back skirting",environment.transform,new Vector3(0,.12f,4.83f),new Vector3(12,.24f,.1f),trim);
            Cube("Left skirting",environment.transform,new Vector3(-5.84f,.12f,0),new Vector3(.1f,.24f,10),trim);
            Cube("Right skirting",environment.transform,new Vector3(5.84f,.12f,0),new Vector3(.1f,.24f,10),trim);
            for(int i=-5;i<=5;i++)Cube("Floor board seam",environment.transform,new Vector3(i,.006f,0),new Vector3(.018f,.008f,10),trim,false);
            Cube("Rug",environment.transform,new Vector3(0,.014f,-.7f),new Vector3(5.8f,.025f,4.6f),teal,false);
            Cube("Sofa base",environment.transform,new Vector3(4.6f,.39f,-1.2f),new Vector3(1.45f,.78f,3.2f),teal);
            Cube("Sofa back",environment.transform,new Vector3(5.2f,.85f,-1.2f),new Vector3(.3f,1.15f,3.4f),teal);
            for(int i=0;i<3;i++)Cube("Sofa cushion",environment.transform,new Vector3(4.45f,.82f,-2.2f+i),new Vector3(1.05f,.18f,.91f),cream);
            Cube("Desk",environment.transform,new Vector3(-4.5f,.84f,1.8f),new Vector3(2.1f,.14f,1.3f),floor);
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Cube("Desk leg",environment.transform,new Vector3(-4.5f+x*.84f,.42f,1.8f+z*.46f),new Vector3(.12f,.84f,.12f),trim);
            Cube("Desk notebook",environment.transform,new Vector3(-4.7f,.95f,1.8f),new Vector3(.6f,.045f,.4f),cream,false);
            Cube("Door frame",environment.transform,new Vector3(3.65f,1.45f,4.83f),new Vector3(1.65f,2.9f,.15f),trim);
            Cube("Maintenance door",environment.transform,new Vector3(3.65f,1.4f,4.7f),new Vector3(1.4f,2.7f,.17f),teal);
            var doorHandle=Primitive("Door handle",PrimitiveType.Sphere,environment.transform,new Vector3(3.12f,1.2f,4.55f),new Vector3(.1f,.1f,.1f),cream,false);
            Cube("Window trim",environment.transform,new Vector3(-5.83f,2, -1.1f),new Vector3(.15f,1.65f,3.1f),trim);
            Cube("Window glow",environment.transform,new Vector3(-5.72f,2,-1.1f),new Vector3(.06f,1.4f,2.85f),Material("WindowGlass",new Color(.22f,.36f,.42f),.7f,new Color(.12f,.21f,.28f)),false);
            Cube("Window divider",environment.transform,new Vector3(-5.65f,2,-1.1f),new Vector3(.08f,1.5f,.08f),cream,false);
            var lightObject=new GameObject("Warm ceiling light",typeof(Light));lightObject.transform.SetParent(environment.transform);lightObject.transform.position=new Vector3(0,3.2f,0);
            var light=lightObject.GetComponent<Light>();light.type=LightType.Point;light.range=15;light.intensity=5;light.color=new Color(1,.82f,.62f);light.shadows=LightShadows.Soft;
            var fill=new GameObject("Window fill",typeof(Light));fill.transform.SetParent(environment.transform);fill.transform.position=new Vector3(-4.7f,2,-1);var fillLight=fill.GetComponent<Light>();fillLight.type=LightType.Point;fillLight.range=11;fillLight.intensity=2.5f;fillLight.color=new Color(.48f,.7f,.9f);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.24f,.28f,.3f);RenderSettings.fog=false;

            var surface=environment.AddComponent<NavMeshSurface>();surface.collectObjects=CollectObjects.Children;surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideTileSize=true;surface.tileSize=128;surface.BuildNavMesh();
            if(surface.navMeshData==null)throw new Exception("NavMesh generation failed");
            const string navPath="Assets/_Before1800/Scenes/E1_NavMesh.asset";
            if(AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath)!=null)AssetDatabase.DeleteAsset(navPath);
            AssetDatabase.CreateAsset(surface.navMeshData,navPath);

            var playerObject=new GameObject("Player",typeof(CharacterController),typeof(FirstPersonPlayer));playerObject.transform.position=new Vector3(0,.02f,-3.2f);
            var character=playerObject.GetComponent<CharacterController>();character.height=1.8f;character.radius=.3f;character.center=new Vector3(0,.9f,0);character.stepOffset=.25f;
            var cameraObject=new GameObject("Player Camera",typeof(Camera),typeof(AudioListener));cameraObject.tag="MainCamera";cameraObject.transform.SetParent(playerObject.transform,false);cameraObject.transform.localPosition=new Vector3(0,1.65f,0);
            var camera=cameraObject.GetComponent<Camera>();camera.fieldOfView=67;camera.nearClipPlane=.07f;camera.farClipPlane=80;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.07f,.09f);
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            var player=playerObject.GetComponent<FirstPersonPlayer>();player.viewCamera=camera;

            var linObject=new GameObject("Lin",typeof(NavMeshAgent),typeof(LinMotor),typeof(InteractionTarget));linObject.transform.position=new Vector3(-1.7f,0,-.8f);linObject.transform.rotation=Quaternion.Euler(0,180,0);
            var agent=linObject.GetComponent<NavMeshAgent>();agent.radius=.3f;agent.height=1.8f;agent.speed=2.3f;agent.acceleration=10;agent.angularSpeed=240;agent.stoppingDistance=.12f;
            agent.enabled=false;
            var linTarget=linObject.GetComponent<InteractionTarget>();linTarget.targetId="lin";linTarget.displayName="Talk to Lin";linTarget.range=3.5f;
            var lin=linObject.GetComponent<LinMotor>();
            var visual=new GameObject("Lin visual placeholder");visual.transform.SetParent(linObject.transform,false);lin.visual=visual.transform;
            var jacket=Material("LinJacket",new Color(.21f,.29f,.32f));var skin=Material("LinSkin",new Color(.74f,.6f,.47f));
            Primitive("Body",PrimitiveType.Capsule,visual.transform,new Vector3(0,.85f,0),new Vector3(.55f,.56f,.38f),jacket,true);
            var head=Primitive("Head",PrimitiveType.Sphere,visual.transform,new Vector3(0,1.52f,0),new Vector3(.4f,.46f,.39f),skin,false);lin.head=head.transform;
            Primitive("Hair",PrimitiveType.Sphere,visual.transform,new Vector3(0,1.68f,-.015f),new Vector3(.41f,.22f,.39f),trim,false);
            for(int sign=-1;sign<=1;sign+=2)
            {
                Primitive("Eye",PrimitiveType.Sphere,head.transform,new Vector3(sign*.19f,.04f,.46f),new Vector3(.12f,.1f,.05f),trim,false);
                Cube("Leg",visual.transform,new Vector3(sign*.15f,.3f,0),new Vector3(.2f,.6f,.23f),trim,true);
                Cube("Shoe",visual.transform,new Vector3(sign*.15f,.08f,.06f),new Vector3(.23f,.15f,.38f),trim,true);
                Primitive("Arm",PrimitiveType.Capsule,visual.transform,new Vector3(sign*.36f,.92f,0),new Vector3(.16f,.35f,.17f),jacket,false);
            }

            var camRoot=new GameObject("E1 - tracking camera",typeof(CameraExperiment),typeof(InteractionTarget));
            var experiment=camRoot.GetComponent<CameraExperiment>();experiment.lin=lin;
            var camTarget=camRoot.GetComponent<InteractionTarget>();camTarget.targetId="camera_01";camTarget.displayName="Examine the tracking camera";camTarget.range=3.5f;
            Cube("Camera mount",camRoot.transform,new Vector3(0,2.65f,4.75f),new Vector3(.14f,.14f,.5f),trim);
            var pivot=new GameObject("Tracking pivot");pivot.transform.SetParent(camRoot.transform);pivot.transform.position=new Vector3(0,2.65f,4.4f);pivot.transform.rotation=Quaternion.LookRotation(head.transform.position-pivot.transform.position);experiment.cameraPivot=pivot.transform;
            Cube("Camera housing",pivot.transform,Vector3.zero,new Vector3(.48f,.28f,.42f),cream);
            var lens=Primitive("Lens",PrimitiveType.Cylinder,pivot.transform,new Vector3(0,0,.26f),new Vector3(.2f,.1f,.2f),black,false);lens.transform.localRotation=Quaternion.Euler(90,0,0);
            Primitive("Recording light",PrimitiveType.Sphere,pivot.transform,new Vector3(.17f,.08f,.22f),new Vector3(.05f,.05f,.05f),glow,false);
            experiment.pointA=Point("Observation A",new Vector3(-2,0,2.2f));experiment.pointB=Point("Observation B",new Vector3(2,0,2.2f));
            foreach(var point in new[]{experiment.pointA,experiment.pointB})
            {
                var marker=Primitive("Observation floor marker",PrimitiveType.Cylinder,camRoot.transform,point.position+Vector3.up*.018f,new Vector3(.7f,.012f,.7f),teal,false);
                Cube("Marker inset",camRoot.transform,point.position+new Vector3(0,.045f,0),new Vector3(.32f,.012f,.08f),glow,false);
            }

            var systems=new GameObject("Before 18 - Prototype",typeof(PrototypeController),typeof(LlmClient));
            var controller=systems.GetComponent<PrototypeController>();controller.player=player;controller.lin=lin;controller.cameraExperiment=experiment;
            controller.playerSpawn=Point("Player spawn",playerObject.transform.position);controller.linSpawn=Point("Lin spawn",linObject.transform.position);
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            PlayerSettings.companyName="COMP5140";PlayerSettings.productName="Before 18 - E1 Prototype";PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();Debug.Log("BEFORE1800_SCENE_READY "+ScenePath);
        }

        [MenuItem("Before 18/Build Windows prototype")]
        public static void BuildWindows()
        {
            if(!File.Exists(ScenePath))CreateScene();
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            string destination=Path.Combine(root,"Builds","Windows","Before1800.exe");Directory.CreateDirectory(Path.GetDirectoryName(destination));
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=destination,target=BuildTarget.StandaloneWindows64,options=BuildOptions.CleanBuildCache});
            File.WriteAllText(Path.Combine(root,"artifacts","build-result.txt"),report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\nBytes: "+report.summary.totalSize);
            if(report.summary.result!=BuildResult.Succeeded || report.summary.totalErrors>0)throw new Exception("Windows build failed: "+report.summary.result+", errors="+report.summary.totalErrors);
            Debug.Log("BEFORE1800_BUILD_READY "+destination);
        }

        private static Transform Point(string name,Vector3 position){var obj=new GameObject(name);obj.transform.position=position;return obj.transform;}
        private static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 scale,Material material,bool collision=true)
        {return Primitive(name,PrimitiveType.Cube,parent,position,scale,material,collision);}
        private static GameObject Primitive(string name,PrimitiveType kind,Transform parent,Vector3 position,Vector3 scale,Material material,bool collision)
        {
            var obj=GameObject.CreatePrimitive(kind);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.localPosition=position;obj.transform.localScale=scale;obj.GetComponent<Renderer>().sharedMaterial=material;
            if(!collision)UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;
        }
        private static Material Material(string name,Color color,float smooth=0,Color? emission=null)
        {
            string path=materials+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",smooth);
            if(emission.HasValue){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",emission.Value);}
            return material;
        }
    }
}
