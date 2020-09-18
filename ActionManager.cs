﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Data.LuminaExtensions;
using Dalamud.Game.Internal;
using Dalamud.Hooking;
using Dalamud.Plugin;
using ImGuiNET;
using ImGuiScene;
using JetBrains.Annotations;
using Action = Lumina.Excel.GeneratedSheets.Action;

namespace RemindMe {
    public class ActionManager : IDisposable {

        private IntPtr actionManagerPtr;
        private RemindMe plugin;

        private Dictionary<uint, Cooldown> cooldownList = new Dictionary<uint, Cooldown>();

        private delegate IntPtr StartCooldownDelegate(IntPtr actionManager, uint actionType, uint actionId);
        private Hook<StartCooldownDelegate> startCooldownHook;
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate bool IsActionCooldownDelegate(IntPtr actionManager, uint actionType, uint actionId);
        private IsActionCooldownDelegate isActionCooldown;

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr GetActionCooldownSlotDelegate(IntPtr actionManager, int cooldownGroup);

#if DEBUG
        public List<ActionHistoryItem> ActionHistory = new List<ActionHistoryItem>();
        public uint ActionHistoryLimit = 100;
#endif
        internal TextureWrap GetActionIcon(Action action) {
            var iconTex = plugin.PluginInterface.Data.GetIcon(action.Icon);
            var tex = plugin.PluginInterface.UiBuilder.LoadImageRaw(iconTex.GetRgbaImageData(), iconTex.Header.Width, iconTex.Header.Height, 4);
            if (tex != null && tex.ImGuiHandle != IntPtr.Zero) {
                return tex;
            }
            return null;
        }

        private GetActionCooldownSlotDelegate getActionCooldownSlot;
        
        public ActionManager(RemindMe plugin, IntPtr actionManagerPtr) {
            this.actionManagerPtr = actionManagerPtr;
            this.plugin = plugin;

            var isActionCooldownScan = plugin.PluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 3C 01 74 45");
            isActionCooldown = Marshal.GetDelegateForFunctionPointer<IsActionCooldownDelegate>(isActionCooldownScan);

            var getActionCooldownSlotScan = plugin.PluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 0F 57 FF 48 85 C0");
            getActionCooldownSlot = Marshal.GetDelegateForFunctionPointer<GetActionCooldownSlotDelegate>(getActionCooldownSlotScan);
#if DEBUG
            var startActionCooldownScan = plugin.PluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? FF 50 18");
            startCooldownHook = new Hook<StartCooldownDelegate>(startActionCooldownScan, new StartCooldownDelegate(StartCooldownDetour));

            startCooldownHook.Enable();
#endif
            plugin.PluginInterface.Framework.OnUpdateEvent += FrameworkOnOnUpdateEvent;
        }
#if DEBUG
        private IntPtr StartCooldownDetour(IntPtr actionManager, uint actionType, uint actionId) {
            ActionHistory.Add(new ActionHistoryItem(actionId, plugin.PluginInterface.ClientState.Targets.CurrentTarget));
            while (ActionHistory.Count > ActionHistoryLimit) {
                ActionHistory.RemoveAt(0);
            }
            return startCooldownHook.Original(actionManager, actionType, actionId);
        }
#endif


        [CanBeNull]
        public Action GetAction(uint actionID) {
            return plugin.ActionList.FirstOrDefault(a => a.RowId == actionID);
        }

        private void FrameworkOnOnUpdateEvent(Framework framework) {
            Update();
        }

        public bool IsActionCooldown(Action action) {
            return action.IsPlayerAction && isActionCooldown(actionManagerPtr, 1, action.RowId);
        }

        public Cooldown GetActionCooldown(Action action) {
            if (cooldownList.ContainsKey(action.RowId)) return cooldownList[action.RowId];
            var cooldown = new Cooldown(this, action);
            cooldownList.Add(action.RowId, cooldown);
            return cooldown;
        }

        public void Dispose() {
            startCooldownHook?.Disable();
            startCooldownHook?.Dispose();
            plugin.PluginInterface.Framework.OnUpdateEvent -= FrameworkOnOnUpdateEvent;

            foreach (var a in cooldownList) {
                a.Value.Dispose();
            }

        }

        public void Update() {
            foreach (var i in cooldownList) {
                i.Value.Update();
            }
        }

        public IntPtr GetCooldownPointer(byte actionCooldownGroup) {
            return getActionCooldownSlot(actionManagerPtr, actionCooldownGroup - 1);
        }
    }
}