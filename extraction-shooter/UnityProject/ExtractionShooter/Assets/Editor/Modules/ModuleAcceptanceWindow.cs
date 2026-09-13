using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GourmetAbyss.CameraSystem;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Modules.Editor
{
    public sealed class ModuleAcceptanceWindow : EditorWindow
    {
        static readonly string[] Steps={"进入餐厅：检查地面透视、两排三列桌椅和 HUD", "拖拽边界：镜头移动但不得越过配置上限", "UI 拦截：指针位于 UI 时不应拖动镜头", "打开菜谱：检查旧菜单是否显示且可操作", "打开装修：检查原升级面板", "关闭菜单并退出：恢复小镇镜头和玩家", "再次进入、禁用模块：检查异常退出恢复"};
        static RestaurantModuleAdapter adapter;
        static CameraDirector director;
        static TopDownController player;
        static int step=-1,requestCount;
        static float originalNear;
        static Vector3 originalPosition;
        static bool originalMovement;
        static Vector2 panBeforeUI;
        static bool autoRun;
        static double nextTime;
        static readonly List<string> results=new List<string>();
        static PlanarSprite[] placedSprites;
        static Quaternion[] placedRotations;
        static Vector3[] placedScales;
        public static string Status {get;private set;}="Not started";
        public static void StopAndRestore() { Finish("Stopped"); }

        [MenuItem("Tools/Modules/Restaurant Acceptance")]
        public static void ShowWindow(){GetWindow<ModuleAcceptanceWindow>("餐厅验收").Show();}
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("先运行 UpGround。自动验收会执行真实进出和菜单事件；拖拽通过输入路由注入。不会添加食材、改金币或写存档。",MessageType.Info);
            EditorGUILayout.LabelField(Status,EditorStyles.wordWrappedLabel);
            if(GUILayout.Button("开始自动验收"))Begin(true);
            if(GUILayout.Button("开始逐步验收"))Begin(false);
            GUI.enabled=step>=0&&!autoRun;
            if(GUILayout.Button("验证当前步并进入下一步"))Advance();
            GUI.enabled=true;
            if(GUILayout.Button("结束并恢复"))Finish("Stopped");
            if(GUILayout.Button("检查独立预制与家具锚点"))Debug.Log(ModuleSupplementalChecks.CheckPortablePrefabs());
            if(GUILayout.Button("测试食材飞行显示（需在餐厅内，不扣资源）"))ModuleSupplementalChecks.BeginFlightProbe();
            foreach(var result in results)EditorGUILayout.LabelField(result,EditorStyles.wordWrappedLabel);
        }
        public static void Begin(bool automatic)
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("请先运行 UpGround。");
            if(step>=0)Finish("Restarted");
            adapter=Object.FindObjectOfType<RestaurantModuleAdapter>();director=CameraService.Active;player=Object.FindObjectOfType<TopDownController>();
            if(adapter==null||director==null||player==null||adapter.entry.IsEntered)throw new InvalidOperationException("需在小镇、餐厅外开始。");
            ModuleTools.ValidateDefinition(adapter.pair.definition);
            placedSprites=adapter.pair.world.GetComponentsInChildren<PlanarSprite>(true);
            placedRotations=placedSprites.Select(s=>s.transform.localRotation).ToArray();
            placedScales=placedSprites.Select(s=>s.transform.localScale).ToArray();
            requestCount=director.RequestCount;originalNear=director.Camera.nearClipPlane;originalPosition=player.transform.position;originalMovement=player.canPlayerMove;
            results.Clear();step=0;autoRun=automatic;Status="Running";
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
            // UGUI raycast depth is populated by rendering; keep Game view visible during automation.
            if (automatic) GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
            Enter();Announce();
        }
        static void Enter()
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(RestaurantEntryPoint).GetField("_cachedPlayer",flags).SetValue(adapter.entry,player);
            typeof(RestaurantEntryPoint).GetMethod("EnterEntryState",flags).Invoke(adapter.entry,null);
        }
        static void Announce(){Status="Running: "+(step+1)+"/"+Steps.Length+" "+Steps[step];Debug.Log("[Module Acceptance] "+Status);nextTime=EditorApplication.timeSinceStartup+3;}
        static void Tick()
        {
            if(step<0)return;
            if(!EditorApplication.isPlaying){Finish("Stopped: Play ended");return;}
            if(autoRun&&EditorApplication.timeSinceStartup>=nextTime)Advance();
        }
        static void Check(bool passed,string message){if(!passed)throw new InvalidOperationException(message);results.Add("PASS: "+message);}
        static void Advance()
        {
            try
            {
                Check(placedSprites.Length>0 && placedSprites.Select((s,i)=>s!=null &&
                    Quaternion.Angle(s.transform.localRotation,placedRotations[i])<.001f &&
                    Vector3.Distance(s.transform.localScale,placedScales[i])<.00001f).All(x=>x),
                    "阶段 "+step+"：全部素材保持摆放角度和缩放，不跟随镜头旋转");
                switch(step)
                {
                    case 0:
                        Check(adapter.entry.IsEntered&&adapter.pair.IsOpen&&!director.Camera.orthographic&&director.Camera.nearClipPlane>0,"餐厅透视已生效，近裁剪面合法");
                        Check(adapter.pair.world.transform.Find("VisualRoot").Cast<Transform>().Count(t=>t.name.StartsWith("TableSet"))==6,"六组桌椅存在");
                        Check(true,ModuleSupplementalChecks.CheckArtworkProjection(director.Camera,placedSprites));
                        Check(adapter.restaurant.allDishQueueSlots.All(s=>s.transform.IsChildOf(adapter.pair.hud.transform)),"原烹饪队列对象保持引用并挂在新 HUD 下");
                        var debug=Object.FindObjectOfType<RunIngredientDebugUI>(true);
                        Check(debug==null||!debug.GetComponent<Canvas>().enabled,"餐厅不叠加旧食材调试界面");
                        Check(adapter.pair.presentation.hideWhileOpen.All(g=>g==null||g.alpha==0),"旧主界面仅隐藏显示，不关闭业务对象");
                        Directory.CreateDirectory("Library/ModuleAcceptance");ScreenCapture.CaptureScreenshot("Library/ModuleAcceptance/01-restaurant.png");
                        if(autoRun)director.InputRouter.SetDebugOverride(new CameraInputFrame{PanHeld=true,PointerPositionPixels=new Vector2(Screen.width*.5f,Screen.height*.5f),PointerDeltaPixels=new Vector2(100,0)});
                        break;
                    case 1:
                        Check(true,ModuleSupplementalChecks.CheckArtworkProjection(director.Camera,placedSprites));
                        Check(adapter.pair.world.view.PanOffset.magnitude<=adapter.pair.world.view.profile.panLimit+.001f&&adapter.pair.world.view.PanOffset.magnitude>.01f,"拖拽发生且受配置上限约束");
                        panBeforeUI=adapter.pair.world.view.PanOffset;
                        if(autoRun)director.InputRouter.SetDebugOverride(new CameraInputFrame{PanHeld=true,PointerBlockedByUi=true,PointerPositionPixels=Vector2.one*100,PointerDeltaPixels=Vector2.one*100});break;
                    case 2:
                        Check(true,ModuleSupplementalChecks.CheckArtworkProjection(director.Camera,placedSprites));
                        Check(Vector2.Distance(panBeforeUI,adapter.pair.world.view.PanOffset)<.001f,"UI 拦截拖拽");director.InputRouter.ClearDebugOverride();
                        ModuleUIProbe.Click(adapter.pair.hud.GetAction("recipes").gameObject);
                        ScreenCapture.CaptureScreenshot("Library/ModuleAcceptance/04-recipes.png");break;
                    case 3:
                        Check(adapter.recipesPopup.IsOpen&&adapter.recipesPopup.content.gameObject.activeInHierarchy,"菜谱按钮打开原经营面板");
                        adapter.recipesPopup.Close();
                        ModuleUIProbe.Click(adapter.pair.hud.GetAction("decoration").gameObject);
                        ScreenCapture.CaptureScreenshot("Library/ModuleAcceptance/05-decoration.png");break;
                    case 4:
                        Check(adapter.decorationPopup.IsOpen&&adapter.decorationPopup.content.gameObject.activeInHierarchy,"装修按钮打开原升级面板");
                        adapter.decorationPopup.Close();
                        ModuleUIProbe.Click(adapter.pair.hud.GetAction("exit").gameObject);
                        ScreenCapture.CaptureScreenshot("Library/ModuleAcceptance/06-return.png");break;
                    case 5:
                        VerifyRestored();Enter();break;
                    case 6:
                        Check(adapter.entry.IsEntered&&adapter.pair.world.view.PanOffset.sqrMagnitude<.001f,"再次进入重置拖拽偏移");
                        adapter.enabled=false;nextTime=EditorApplication.timeSinceStartup+2;step=7;return;
                    case 7:
                        VerifyRestored();adapter.enabled=true;Finish("PASS");return;
                }
                step++;Announce();
            }
            catch(Exception e){results.Add("FAIL: "+e.Message);Finish("FAIL: "+e.Message);Debug.LogError("[Module Acceptance] "+e);}
        }
        static void VerifyRestored()
        {
            Check(!adapter.entry.IsEntered&&!adapter.pair.IsOpen&&director.Camera.orthographic,"退出恢复小镇正交镜头与 HUD 状态");
            Check(Mathf.Abs(director.Camera.nearClipPlane-originalNear)<.001f,"恢复原近裁剪面");
            Check(director.RequestCount==requestCount,"无残留镜头请求");
            Check(player.canPlayerMove==originalMovement&&Vector3.Distance(player.transform.position,originalPosition)<.5f,"玩家移动状态和门口位置恢复");
        }
        static void Finish(string status)
        {
            EditorApplication.update-=Tick;step=-1;autoRun=false;Status=status;
            if(director!=null)director.InputRouter.ClearDebugOverride();
            if(adapter!=null){if(adapter.entry.IsEntered)adapter.entry.LeaveRestaurant();adapter.enabled=true;}
            Directory.CreateDirectory("Library/ModuleAcceptance");
            File.WriteAllText("Library/ModuleAcceptance/Report.txt",status+"\n"+string.Join("\n",results)+"\nNOT COVERED: full ingredient/cooking/customer/economy persistence; visual approval.\n");
            Debug.Log("[Module Acceptance] "+status);
        }
    }
}
