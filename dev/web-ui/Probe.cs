using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 에디터에서 script-execute로만 실행하는 검증 도구. 게임 빌드에는 포함하지 않는다.
public class WebUiVerification20260831 : MonoBehaviour
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Out = "Builds/UI-verification";
    readonly List<object> results = new List<object>();
    string originalLanguage;
    bool languageWasSaved;
    string savedLanguage;
    string languageKey;
    string savedUnlocks;
    bool unlocksWereSaved;
    bool protect;

    public static string Run()
    {
        if (!EditorApplication.isPlaying) return "플레이 모드 필요";
        var old = GameObject.Find("WebUiProbe");
        if (old != null) Object.DestroyImmediate(old);
        var go = new GameObject("WebUiProbe");
        Object.DontDestroyOnLoad(go);
        var probe = go.AddComponent<WebUiVerification20260831>();
        probe.StartCoroutine(probe.Guarded());
        return "웹 UI 검증 시작";
    }

    IEnumerator Guarded()
    {
        Directory.CreateDirectory(Out);
        File.WriteAllText(Out + "/error.txt", "");
        File.WriteAllText(Out + "/done.txt", "검증 진행 중");
        originalLanguage = Loc.CurrentCode;
        languageKey = (string)typeof(Loc).GetField("PrefsKey", Flags).GetRawConstantValue();
        languageWasSaved = PlayerPrefs.HasKey(languageKey);
        savedLanguage = PlayerPrefs.GetString(languageKey);
        unlocksWereSaved = PlayerPrefs.HasKey("unlock_state_v1");
        savedUnlocks = PlayerPrefs.GetString("unlock_state_v1");
        IEnumerator run = Audit();
        while (true)
        {
            object next;
            try { if (!run.MoveNext()) break; next = run.Current; }
            catch (Exception e) { File.WriteAllText(Out + "/error.txt", e.ToString()); break; }
            yield return next;
        }
        Loc.SetLanguage(originalLanguage);
        if (languageWasSaved) PlayerPrefs.SetString(languageKey, savedLanguage);
        else PlayerPrefs.DeleteKey(languageKey);
        if (unlocksWereSaved) PlayerPrefs.SetString("unlock_state_v1",savedUnlocks);
        else PlayerPrefs.DeleteKey("unlock_state_v1");
        foreach(string field in new[]{"counters","distinctSets","unlockedItemIds"})
        {
            object collection=typeof(UnlockState).GetField(field,Flags).GetValue(null);
            collection.GetType().GetMethod("Clear").Invoke(collection,null);
        }
        typeof(UnlockState).GetField("loaded",Flags).SetValue(null,false);
        typeof(UnlockState).GetField("dirty",Flags).SetValue(null,false);
        PlayerPrefs.Save();
        File.WriteAllText(Out + "/report.json", JsonConvert.SerializeObject(results, Formatting.Indented));
        File.WriteAllText(Out + "/done.txt", DateTime.Now.ToString("s"));
        protect = false;
        SceneManager.LoadScene("Title");
    }

    void Update()
    {
        if (!protect) return;
        var player = Object.FindFirstObjectByType<PlayerRobotController>();
        if (player != null) player.Heal(player.MaxHp);
        var spawner = Object.FindFirstObjectByType<EnemySpawner>();
        if (spawner != null) spawner.enabled = false;
    }

    IEnumerator Audit()
    {
        var sizes = new[] { new Vector2Int(1920,1080), new Vector2Int(960,600), new Vector2Int(960,540), new Vector2Int(640,360), new Vector2Int(800,600) };
        foreach (string lang in new[] { "ko", "en" })
        foreach (var size in sizes)
        {
            File.WriteAllText(Out + "/progress.txt", lang + " " + size);
            Loc.SetLanguage(lang);
            Resize(size.x, size.y);
            protect = false;
            SceneManager.LoadScene("Title");
            yield return new WaitForSecondsRealtime(.7f);
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var title = Object.FindFirstObjectByType<TitleSceneManager>();
            string prefix = lang + "_" + size.x + "x" + size.y + "_";
            Capture(prefix + "title", canvas.transform);
            Call(title, "OnLanguageClicked");
            yield return new WaitForSecondsRealtime(.2f);
            var language = Object.FindFirstObjectByType<LanguageSelectPanelUI>();
            Capture(prefix + "language", language.transform); language.Close();
            var settings = SettingsPanelUI.Attach((RectTransform)canvas.transform); settings.Open();
            yield return new WaitForSecondsRealtime(.2f);
            Capture(prefix + "settings", settings.transform); settings.Close();
            Call(title,"OnRankingClicked");
            yield return new WaitForSecondsRealtime(.2f);
            var ranking = Object.FindFirstObjectByType<RankingPanelUI>();
            Capture(prefix + "ranking", ranking.transform); ranking.Close();
            Call(title,"OnCollectionClicked");
            yield return new WaitForSecondsRealtime(.2f);
            var collection = Object.FindFirstObjectByType<CollectionPanelUI>();
            Capture(prefix + "collection", collection.transform); Call(collection,"Close");
            Call(title,"OnStartClicked");
            yield return new WaitForSecondsRealtime(.2f);
            Capture(prefix + "heads", Object.FindFirstObjectByType<HeadSelectPanelUI>().transform);

            protect = true;
            SceneManager.LoadScene("Ground01");
            yield return new WaitForSecondsRealtime(1.7f);
            GameFlowManager.SetTimeScale(0);
            canvas = Object.FindFirstObjectByType<Canvas>();
            Capture(prefix + "hud", canvas.transform);
            var flow = Object.FindFirstObjectByType<GameFlowManager>();
            var pause = PauseMenuUI.Instance;
            Call(pause,"OpenPause");
            yield return new WaitForSecondsRealtime(.2f);
            Capture(prefix + "pause", pause.transform);
            Call(pause,"OpenSettings");
            yield return new WaitForSecondsRealtime(.2f);
            var pauseSettings = Object.FindFirstObjectByType<SettingsPanelUI>();
            Capture(prefix + "pause_settings", pauseSettings.transform); pauseSettings.Close();
            Call(pause,"ClosePause");
            RunState.Gold = 1234567;
            RunState.PendingCoreUpgradeChoices = 1;
            typeof(GameFlowManager).GetProperty("IsIntermission",Flags).SetValue(null,true);
            Call(flow,"ShowAiCoreUpgradeStep");
            yield return new WaitForSecondsRealtime(.3f);
            Capture(prefix + "core", Get<GameObject>(flow,"aiCoreUpgradePanel").transform);
            Call(flow,"CloseAllIntermissionPanels");
            RunState.UnopenedPartBoxCount = 20;
            var modding = Object.FindFirstObjectByType<ModdingPanelUI>(FindObjectsInactive.Include);
            modding.Open();
            yield return new WaitForSecondsRealtime(.3f);
            Capture(prefix + "modding", modding.transform); modding.Close();
            Call(flow,"ShowShop");
            yield return new WaitForSecondsRealtime(.4f);
            var shop = Object.FindFirstObjectByType<ShopPanelUI>();
            Capture(prefix + "shop", shop.transform);
            Call(shop,"ShowDetail", "head");
            yield return new WaitForSecondsRealtime(.2f);
            var detail = Object.FindFirstObjectByType<EquipmentDetailPopup>();
            if (detail != null) { Capture(prefix + "detail", detail.transform); detail.Hide(); }
            Call(shop,"HandleRefreshClicked");
            yield return new WaitForSecondsRealtime(.2f);
            Capture(prefix + "shop_refresh", shop.transform);
            // 구매 카드의 무기 품목을 찾아 소켓 선택 UI도 검사한다.
            for (int i=0; i<4; i++)
            {
                var offer=Object.FindFirstObjectByType<ShopManager>().Offers[i];
                if (offer.IsDisc || offer.IsAccessory) continue;
                Call(shop,"HandleOfferClicked",i);
                var picker = Get<GameObject>(shop,"socketPickerRoot");
                if (picker != null && picker.activeSelf) { yield return new WaitForSecondsRealtime(.2f); Capture(prefix + "socket_picker",picker.transform); break; }
            }
            shop.Close();
            var summary = Object.FindFirstObjectByType<GameOverSummaryUI>(FindObjectsInactive.Include);
            typeof(GameOverSummaryUI).GetField("scoreSubmitted",Flags).SetValue(summary,true);
            summary.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(.2f);
            Capture(prefix + "gameover",summary.transform);
            var nickname = NicknameInputPopup.Attach((RectTransform)canvas.transform, RunScore.ResolveDefaultPlayerName(), _=>{});
            yield return new WaitForSecondsRealtime(.2f);
            Capture(prefix + "nickname",nickname.transform); Object.Destroy(nickname.gameObject);
            summary.gameObject.SetActive(false);
            var score = ScoreSummaryPopup.EnsureAttached((RectTransform)canvas.transform);
            score.ShowClearChoice(20,()=>{},()=>{});
            yield return new WaitForSecondsRealtime(.2f);
            Capture(prefix + "score",score.transform);
        }

        // 같은 패널을 유지한 채 축소·확대·복귀한다. 생성 시점에만 맞는 레이아웃 회귀를 검사한다.
        protect = true;
        var catalog=AssetDatabase.LoadAssetAtPath<PartsCatalog>("Assets/Data/PartsCatalog.asset");
        int originalRobot=PlayerSession.SelectedRobotId;
        PlayerSession.SelectedRobotId=catalog.GetSelectableHeads().OrderByDescending(h=>h.weaponSocketCount).First().robotId;
        SceneManager.LoadScene("Ground01");
        yield return new WaitForSecondsRealtime(1.7f);
        GameFlowManager.SetTimeScale(0);
        var resizeFlow=Object.FindFirstObjectByType<GameFlowManager>();
        RunState.Gold=9876543;
        Call(resizeFlow,"ShowShop");
        var resizeShop=Object.FindFirstObjectByType<ShopPanelUI>();
        int cycle=0;
        foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(960,540),new Vector2Int(640,360),new Vector2Int(2560,1440),new Vector2Int(1920,1080)})
        {
            Resize(size.x,size.y);
            yield return new WaitForSecondsRealtime(.7f);
            Capture("resize_"+(cycle++)+"_"+size.x+"x"+size.y,resizeShop.transform);
        }
        for(int i=0;i<3;i++)
        {
            resizeShop.Close(); resizeShop.Open();
            RunState.Gold=i==0?0:i==1?1:1234567; RunState.NotifyChanged();
            yield return new WaitForSecondsRealtime(.3f);
            Capture("reopen_"+i,resizeShop.transform);
        }
        PlayerSession.SelectedRobotId=originalRobot;
    }

    public static void Resize(int width, int height)
    {
        Assembly a=typeof(EditorWindow).Assembly;
        Type sizesType=a.GetType("UnityEditor.GameViewSizes");
        Type singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        object sizes=singleton.GetProperty("instance",Flags).GetValue(null);
        Type viewType=a.GetType("UnityEditor.GameView");
        EditorWindow view=EditorWindow.GetWindow(viewType);
        object groupType=viewType.GetProperty("currentSizeGroupType",Flags).GetValue(view);
        object group=sizesType.GetMethod("GetGroup",Flags).Invoke(sizes,new[]{groupType});
        Type sizeType=a.GetType("UnityEditor.GameViewSize");
        Type kind=a.GetType("UnityEditor.GameViewSizeType");
        string label="웹 UI 검증 " + width + "×" + height;
        int count=(int)group.GetType().GetMethod("GetTotalCount",Flags).Invoke(group,null);
        int index=-1;
        for(int i=0;i<count;i++)
        {
            object item=group.GetType().GetMethod("GetGameViewSize",Flags).Invoke(group,new object[]{i});
            if ((int)sizeType.GetProperty("width",Flags).GetValue(item)==width && (int)sizeType.GetProperty("height",Flags).GetValue(item)==height) { index=i; break; }
        }
        if(index<0)
        {
            object size=Activator.CreateInstance(sizeType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{Enum.Parse(kind,"FixedResolution"),width,height,label},null);
            group.GetType().GetMethod("AddCustomSize",Flags).Invoke(group,new[]{size});
            index=count;
        }
        viewType.GetProperty("selectedSizeIndex",Flags).SetValue(view,index);
        view.Repaint();
    }

    void Capture(string name, Transform root)
    {
        Canvas.ForceUpdateCanvases();
        var texts = new List<object>();
        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (!t.enabled || string.IsNullOrWhiteSpace(t.text)) continue;
            t.ForceMeshUpdate();
            var r=t.rectTransform.rect;
            texts.Add(new { path=PathOf(t.transform), text=t.text, width=r.width,height=r.height, margin=t.margin.ToString(), font=t.fontSize,
                characters=t.textInfo.characterCount, visible=t.textInfo.characterInfo.Take(t.textInfo.characterCount).Count(c=>c.isVisible), overflow=t.isTextOverflowing });
        }
        var buttons=root.GetComponentsInChildren<Button>().Select(b=>new {path=PathOf(b.transform),enabled=b.interactable,rect=((RectTransform)b.transform).rect.ToString()}).ToArray();
        results.Add(new { name, screen=new[]{Screen.width,Screen.height},texts,buttons });
        Type type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var view=EditorWindow.GetWindow(type);
        var rt=(RenderTexture)type.GetMethod("RenderView",Flags).Invoke(view,new object[]{new Vector2(Screen.width,Screen.height),false});
        if(rt==null) throw new Exception("GameView 렌더 텍스처 없음");
        var old=RenderTexture.active; RenderTexture.active=rt;
        var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); tex.Apply();
        if (SystemInfo.graphicsUVStartsAtTop)
        {
            Color32[] pixels=tex.GetPixels32();
            for(int y=0;y<rt.height/2;y++)
            for(int x=0;x<rt.width;x++)
            {
                int top=y*rt.width+x, bottom=(rt.height-y-1)*rt.width+x;
                Color32 pixel=pixels[top]; pixels[top]=pixels[bottom]; pixels[bottom]=pixel;
            }
            tex.SetPixels32(pixels); tex.Apply();
        }
        File.WriteAllBytes(Out+"/"+name+".png",tex.EncodeToPNG());
        RenderTexture.active=old; Object.Destroy(tex);
    }

    static string PathOf(Transform t) => t.parent==null ? t.name : PathOf(t.parent)+"/"+t.name;
    static T Get<T>(object o,string name) => (T)o.GetType().GetField(name,Flags).GetValue(o);
    static object Call(object o,string name,params object[] args) => o.GetType().GetMethod(name,Flags).Invoke(o,args);
}

