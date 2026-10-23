#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Modules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class RestaurantUiAcceptance
{
    const string Key = "RestaurantUiAcceptance.";
    static RestaurantModuleAdapter adapter;
    static RestaurantPanel panel;
    static RestaurantIngredientStorage stock;
    static DishRecipe recipe;
    static Dictionary<ResourceType,long> costs;
    static Dictionary<ResourceType,long> before;
    static List<Pot> pots;
    static string Phase => SessionState.GetString(Key+"phase", "");
    static RestaurantUiAcceptance()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (message, stack, type) => {
            if (Phase != "" && Phase != "exit" && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                SessionState.SetString(Key+"error", message);
        };
    }
    public static void Run()
    {
        SessionState.EraseString(Key+"error"); SessionState.SetInt(Key+"result",1);
        PlayerSettings.companyName="CodexValidation"; PlayerSettings.productName="RestaurantUiAcceptance";
        EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity");
        Next("start",0); EditorApplication.EnterPlaymode();
    }
    public static void RunAll() { SessionState.SetBool(Key+"chainStageOne",true); Run(); }
    public static void RepairAndRunAll()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity");
        Game.Modules.Editor.RestaurantAcceptanceRepair.Apply();
        RunAll();
    }
    static void Next(string phase, double delay = .2)
    {
        SessionState.SetString(Key+"phase",phase);
        SessionState.SetFloat(Key+"ready",(float)(EditorApplication.timeSinceStartup+delay));
        SessionState.SetFloat(Key+"deadline",(float)(EditorApplication.timeSinceStartup+75));
    }
    static void Tick()
    {
        if (Phase=="")return;
        if(Phase=="exit")
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetString(Key+"phase","");
                bool chain=SessionState.GetBool(Key+"chainStageOne",false); SessionState.SetBool(Key+"chainStageOne",false);
                if(chain && SessionState.GetInt(Key+"result",1)==0) RestaurantStage1UnityValidation.Run();
                else EditorApplication.Exit(SessionState.GetInt(Key+"result",1));
            }
            return;
        }
        if(!EditorApplication.isPlaying)return;
        EditorApplication.QueuePlayerLoopUpdate();
        try {
            Check(SessionState.GetString(Key+"error","")=="","Runtime error: "+SessionState.GetString(Key+"error",""));
            Check(EditorApplication.timeSinceStartup<SessionState.GetFloat(Key+"deadline",0),"Timeout: "+Phase);
            if(EditorApplication.timeSinceStartup<SessionState.GetFloat(Key+"ready",0))return;
            switch(Phase){
                case "start": Next("prepare",3);break;
                case "prepare": Prepare();break;
                case "entered": Entered();break;
                case "open-menu": Click(adapter.pair.hud.GetAction("cooking"));Next("menu");break;
                case "menu": CookFromMenu();break;
                case "queued": Queued();break;
                case "open-failure-menu": Click(adapter.pair.hud.GetAction("recipes"));Next("failure-menu");break;
                case "failure-menu": StartFailure();break;
                case "refunded": Refunded();break;
                case "exit-check": ExitCheck();break;
                case "recook-menu": Recook();break;
                case "cooking": Cooking();break;
                case "cooking-started": CookingStarted();break;
                case "final-menu": Click(adapter.pair.hud.GetAction("cooking"));Next("final-capture");break;
                case "final-capture": Capture("recipe-menu",1920,1080);Debug.Log("RESTAURANT_UI_POINTER_ACCEPTANCE_PASS");Finish(true);break;
            }
        } catch(Exception error){Debug.LogException(error);Finish(false);}
    }
    static void Prepare()
    {
        adapter=Object.FindObjectOfType<RestaurantModuleAdapter>(true); panel=adapter.restaurant;
        stock=RestaurantIngredientStorage.EnsureExists();
        // Match normal manual entry after dismissing the existing first-run instructions.
        var tutorial=GameObject.Find("TeachPanel");
        if(tutorial!=null)
        {
            Click(tutorial.GetComponentInChildren<Button>());
            Check(!tutorial.activeInHierarchy,"The existing tutorial close button must dismiss its panel.");
        }
        Check(GameObject.Find("MainUI").transform.Find("MainTab")==null,"Obsolete navigation must be deleted.");
        Check(adapter.pair.hud.transform.Find("MoneyPanel")==null && adapter.pair.hud.GetText("money")==null,"Duplicate currency panel must be deleted.");
        var home=Object.FindObjectOfType<HomeUIManager>();
        Check(home.textMoneyVal.transform.parent.GetComponent<Image>().sprite.name=="金币图标·","Existing currency UI must use the new coin art.");
        recipe=panel.dishRecipes.First(x=>!x.locked && x.ingredients.Count>0 && x.ingredients.All(i=>ResourceStorageRules.IsIngredient(i.resourceType)) && x.acceptablePot.Count>0);
        Check(RestaurantIngredientStorage.TryGetRecipeCosts(recipe,out costs),"Actual recipe costs are valid.");
        InventoryManager.Instance.ClearAllItems(); WeaponStatsManager.Instance.SetInventorySlotCount(8); WeaponStatsManager.Instance.SetInventorySlotCapacity(100);
        foreach(var cost in costs)Check(InventoryManager.Instance.AddItemPartial(cost.Key,(int)cost.Value*10)==cost.Value*10,"Ingredient fixture must fit in actual backpack.");
        Check(InventoryManager.Instance.InventoryPresentationRoot.activeInHierarchy,"Backpack is visible before entry.");
        var player=Object.FindObjectsOfType<TopDownController>().First(x=>x.CompareTag("Player"));
        Check(adapter.entry.TryEnterRestaurant(player),"Actual entry succeeds.");
        pots=new List<Pot>(panel.potsList);
        Next("entered",1);
    }
    static void Entered()
    {
        Check(!InventoryManager.Instance.InventoryPresentationRoot.activeSelf,"Backpack hides after transfer.");
        foreach(var c in costs)Check(stock.GetCount(c.Key)==c.Value*10,"Exact ingredient deposit.");
        var floor=adapter.pair.world.transform.Find("VisualRoot/Ground").GetComponentsInChildren<SpriteRenderer>().First(x=>x.enabled);
        var grass=adapter.pair.world.transform.Find("VisualRoot/SurroundingGround").GetComponentsInChildren<SpriteRenderer>().First(x=>x.enabled);
        Check(floor.sortingLayerID==grass.sortingLayerID && floor.sortingOrder>grass.sortingOrder,"Grass must sort behind the restaurant floor.");
        Canvas.ForceUpdateCanvases();
        foreach(var slot in panel.allDishQueueSlots)Check(((RectTransform)slot.transform).rect.width>100,"Actual slot layout must survive serialization.");
        for(int i=1;i<panel.allDishQueueSlots.Count;i++)Check(((RectTransform)panel.allDishQueueSlots[i].transform).anchoredPosition.x-((RectTransform)panel.allDishQueueSlots[i-1].transform).anchoredPosition.x>100,"Queue slots must not overlap.");
        Capture("restaurant",1920,1080); Capture("restaurant-720",1280,720); Capture("restaurant-1440",2560,1440);
        Next("open-menu");
    }
    static dishItemPrefabs Widget()=>panel.dishParent.GetComponentsInChildren<dishItemPrefabs>().First(x=>x.recipeData==recipe);
    static void ClickCook()
    {
        var widget=Widget();
        if(!widget.cookDishButton.activeInHierarchy)Click(widget.DishBG.GetComponent<Button>());
        Click(widget.cookDishButton.GetComponent<Button>());
    }
    static void CookFromMenu()
    {
        Check(adapter.recipesPopup.IsOpen && adapter.recipesPopup.content.gameObject.activeInHierarchy,"Clicking the top queue opens the actual menu.");
        before=costs.ToDictionary(x=>x.Key,x=>stock.GetCount(x.Key));
        // Hold the real pots busy so this case exercises visible waiting slots, not immediate cooking.
        foreach(var pot in panel.potsList) pot.potState=potState.Used;
        ClickCook();
        foreach(var c in costs)Check(stock.GetCount(c.Key)==before[c.Key]-c.Value,"Pointer click must reserve ingredients.");
        Dismiss(); Next("queued",1);
    }
    static void Queued()
    {
        Check(CountRecipe()==1,"The actual UI click must enqueue one meal.");
        var slot=panel.allDishQueueSlots[0];
        Check(slot.GetComponentsInChildren<Image>().Any(x=>x.enabled && x.sprite==recipe.dishIcon),"Top queue displays the actual meal icon.");
        Check(slot.GetComponentsInChildren<Text>().Any(x=>x.text=="1"),"Top queue displays its count.");
        Capture("queued",1920,1080);
        ClearQueue(); Next("open-failure-menu");
    }
    static void StartFailure()
    {
        before=costs.ToDictionary(x=>x.Key,x=>stock.GetCount(x.Key));
        ClickCook();
        foreach(object row in Queue()) { row.GetType().GetField("recipe").SetValue(row,new DishRecipe{dishID=9999,dishName="Other"});row.GetType().GetField("count").SetValue(row,int.MaxValue); }
        Next("refunded",1);
    }
    static void Refunded()
    {
        foreach(var c in costs)Check(stock.GetCount(c.Key)==before[c.Key],"Queue filled during flight must refund exactly once.");
        Check(CountRecipe()==0,"Rejected order must not remain queued.");
        var current=costs.ToDictionary(x=>x.Key,x=>stock.GetCount(x.Key));
        ClickCook();
        foreach(var c in costs)Check(stock.GetCount(c.Key)==current[c.Key],"A full queue cannot debit materials on click.");
        ClearQueue(); Dismiss(); Click(adapter.pair.hud.GetAction("exit")); Next("exit-check");
    }
    static void ExitCheck()
    {
        Check(!adapter.entry.IsEntered && InventoryManager.Instance.InventoryPresentationRoot.activeInHierarchy,"Exit button restores the backpack.");
        foreach(var c in costs)Check(stock.GetCount(c.Key)==before[c.Key],"Exit cannot replay refunds.");
        Check(adapter.entry.TryEnterRestaurant(Object.FindObjectsOfType<TopDownController>().First(x=>x.CompareTag("Player"))),"Empty bag can re-enter.");
        Next("recook-menu");
    }
    static void Recook(){Click(adapter.pair.hud.GetAction("cooking"));Next("cooking",.25);}
    static void Cooking()
    {
        foreach(var pot in panel.potsList) pot.potState=potState.unUsed;
        ClickCook(); Dismiss();
        Next("cooking-started",1.1);
    }
    static void CookingStarted()
    {
        Check(pots.Any(x=>x != null && x.currentRecipe != null && x.currentRecipe.dishID==recipe.dishID && x.potState==potState.Used),"The actual menu order must start cooking in an available pot.");
        Capture("cooking",1920,1080);
        Next("final-menu");
    }
    static IList Queue()=>(IList)typeof(RestaurantPanel).GetField("_cookQueueData",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel);
    static void ClearQueue(){foreach(object row in Queue())row.GetType().GetMethod("Clear").Invoke(row,null);}
    static int CountRecipe(){int count=0;foreach(object row in Queue())if(ReferenceEquals(row.GetType().GetField("recipe").GetValue(row),recipe))count+=(int)row.GetType().GetField("count").GetValue(row);return count;}
    static void Click(Button button)
    {
        Check(button!=null && button.gameObject.activeInHierarchy && button.IsInteractable(),"Actual button must be usable.");
        Canvas.ForceUpdateCanvases();
        var rt=(RectTransform)button.transform;
        Vector2 point=ModuleScreenFlight.ScreenPoint(rt,rt.position);
        var hits=new List<RaycastResult>();var data=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
        EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count>0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)==button.gameObject,"Button is blocked or outside the viewport: "+button.name+" at "+point+" hit "+(hits.Count>0?Hierarchy(hits[0].gameObject.transform):"none"));
        ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerClickHandler);
    }
    static void Dismiss()
    {
        var host=GameObject.Find("LegacyModulePopup"); var button=host.transform.Find("Dismiss").GetComponent<Button>();
        var data=new PointerEventData(EventSystem.current){position=new Vector2(2,2),button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count>0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)==button.gameObject,"Click outside menu should dismiss it.");
        ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerClickHandler);
    }
    static void Capture(string name,int width,int height)
    {
        // Render real world and the currently visible UI; restore all canvas state after capture.
        var cam=new GameObject("AcceptanceCaptureCamera").AddComponent<Camera>();cam.CopyFrom(Camera.main);cam.enabled=false;
        cam.transform.SetPositionAndRotation(Camera.main.transform.position,Camera.main.transform.rotation);cam.cullingMask|=1<<31;cam.aspect=(float)width/height;
        var target=new RenderTexture(width,height,24);target.Create();cam.targetTexture=target;
        var canvases=new[]{adapter.pair.hud.GetComponent<Canvas>(),GameObject.Find("MainUI").GetComponent<Canvas>(),GameObject.Find("LegacyModulePopup")?.GetComponent<Canvas>()}.Where(x=>x!=null && x.enabled && x.gameObject.activeInHierarchy).ToArray();
        var modes=canvases.Select(x=>x.renderMode).ToArray();var cameras=canvases.Select(x=>x.worldCamera).ToArray();var planes=canvases.Select(x=>x.planeDistance).ToArray();
        var orders=canvases.Select(x=>x.sortingOrder).ToArray();var overrides=canvases.Select(x=>x.overrideSorting).ToArray();
        var uiObjects=canvases.SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();var layers=uiObjects.Select(x=>x.gameObject.layer).ToArray();
        try{
            foreach(var t in uiObjects)t.gameObject.layer=31;
            foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=cam;c.planeDistance=1f;c.overrideSorting=true;c.sortingOrder=30000+Mathf.Clamp(c.sortingOrder,-100,500);}
            Canvas.ForceUpdateCanvases();
            if(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=target});else cam.Render();
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();RenderTexture.active=previous;
            string folder=Path.GetFullPath(Application.dataPath+"/../../restaurant-ui");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());Object.DestroyImmediate(image);
        }finally{for(int i=0;i<uiObjects.Length;i++)uiObjects[i].gameObject.layer=layers[i];for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=modes[i];canvases[i].worldCamera=cameras[i];canvases[i].planeDistance=planes[i];canvases[i].sortingOrder=orders[i];canvases[i].overrideSorting=overrides[i];}Canvas.ForceUpdateCanvases();cam.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(cam.gameObject);}
    }
    static void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
    static string Hierarchy(Transform t)=>t.parent==null?t.name:Hierarchy(t.parent)+"/"+t.name;
    static void Finish(bool pass){SessionState.SetInt(Key+"result",pass?0:1);Next("exit");EditorApplication.ExitPlaymode();}
}
#endif
