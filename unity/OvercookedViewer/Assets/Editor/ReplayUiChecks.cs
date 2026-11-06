using System;
using System.Reflection;
using UnityEngine;
using OvercookedReplay;

public static class ReplayUiChecks {
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static void State() {
        var panel=new ViewerSettingsPanel();var current=new ViewerPreferences{width=1920,height=1080};
        panel.Show(current);
        var field=typeof(ViewerSettingsPanel).GetField("draft",BindingFlags.NonPublic|BindingFlags.Instance);
        var draft=(ViewerPreferences)field.GetValue(panel);draft.msaa=8;
        Require(current.msaa==4,"Editing settings changed live preferences before applying");
        var cancel=typeof(ViewerSettingsPanel).GetMethod("Cancel");
        Require(cancel!=null,"Settings need a shared cancel path for button and Escape");
        cancel.Invoke(panel,null);Require(!panel.Open&&current.msaa==4,"Cancel changed live settings");
        panel.Show(current);draft=(ViewerPreferences)field.GetValue(panel);draft.msaa=8;
        var apply=typeof(ViewerSettingsPanel).GetMethod("TryApply");Require(apply!=null,"Settings apply path is missing");
        apply.Invoke(panel,new object[]{(Action<ViewerPreferences>)(p=>throw new InvalidOperationException("test save failure"))});
        Require(panel.Open&&current.msaa==4,"Failed settings apply silently closed the panel");
        ViewerPreferences saved=null;
        apply.Invoke(panel,new object[]{(Action<ViewerPreferences>)(p=>saved=p)});
        Require(!panel.Open&&saved.msaa==8&&current.msaa==4,"Applied settings were not isolated and saved");
        foreach(var screen in new[]{new Vector2(1366,768),new Vector2(1920,1080),new Vector2(1920,1200),new Vector2(3840,2160)}) {
            float height=ReplayUiLayout.Height(screen);var sidebar=ReplayUiLayout.Sidebar(height);var modal=ReplayUiLayout.Settings(height);var transport=ReplayUiLayout.Transport(height);
            Require(sidebar.yMin>=0&&sidebar.yMax<=height&&modal.yMin>=0&&modal.yMax<=height,"UI clipped at "+screen);
            Require(transport.yMin>=height*.83f&&transport.yMax<=height,"Transport overlaps the playable viewport");
            float scale=ReplayUiLayout.Scale(screen.x);
            Require(!CameraNavigation.AllowsInput(new Vector2(500*scale,screen.y-40*scale),screen,false),"Toolbar accepted camera gestures");
            Require(!CameraNavigation.AllowsInput(new Vector2(500*scale,screen.y-90*scale),screen,false),"Navigation hint accepted camera gestures");
            Require(!CameraNavigation.AllowsInput(new Vector2(500*scale,50*scale),screen,false),"Transport accepted camera gestures");
            Require(!CameraNavigation.AllowsInput(new Vector2(500*scale,screen.y*.5f),screen,true),"Settings modal accepted camera gestures");
        }
        Debug.Log("Replay UI settings transaction and four-resolution layout checks passed");
    }
    public static void Build(){State();CameraLandscapeChecks.Navigation();CameraLandscapeChecks.Gestures();NativeDialogChecks.Build();}
}
