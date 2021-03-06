﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using LobbyClient;
using PlasmaDownloader;
using PlasmaShared.ContentService;
using PlasmaShared.UnitSyncLib;

namespace ZeroKLobby.Notifications
{
    public partial class MissionBar : UserControl, INotifyBar
    {
        readonly string missionName;

        public MissionBar(string missionName)
        {
            this.missionName = missionName;
            InitializeComponent();
        }


        NotifyBarContainer container;

        public void AddedToContainer(NotifyBarContainer container) {
            this.container = container;
        }

        public void CloseClicked(NotifyBarContainer container) {
            Program.NotifySection.RemoveBar(this);
        }

        public void DetailClicked(NotifyBarContainer container) {
        }

        public Control GetControl() {
            return this;
        }

        private void MissionBar_Load(object sender, EventArgs e)
        {
            label1.Text = string.Format("Starting mission {0} - please wait", missionName);
            var down = Program.Downloader.GetResource(DownloadType.MOD, missionName);

            PlasmaShared.Utils.StartAsync(() =>
            {
                var metaWait = new EventWaitHandle(false, EventResetMode.ManualReset);
                Mod modInfo = null;
                Program.SpringScanner.MetaData.GetModAsync(missionName,
                                                           mod =>
                                                           {
                                                               if (!mod.IsMission)
                                                               {
                                                                   Program.MainWindow.InvokeFunc(() =>
                                                                   {
                                                                       label1.Text = string.Format("{0} is not a valid mission", missionName);
                                                                       container.btnStop.Enabled = true;
                                                                   });
                                                               }

                                                               else modInfo = mod;

                                                               metaWait.Set();
                                                           },
                                                           error =>
                                                           {
                                                               Program.MainWindow.InvokeFunc(() =>
                                                               {
                                                                   label1.Text = string.Format("Download of metadata failed: {0}", error.Message);
                                                                   container.btnStop.Enabled = true;
                                                               });
                                                               metaWait.Set();
                                                           }, Program.SpringPaths.SpringVersion);
                if (down != null) WaitHandle.WaitAll(new WaitHandle[] { down.WaitHandle, metaWait });
                else metaWait.WaitOne();

                if (down != null && down.IsComplete == false)
                {
                    Program.MainWindow.InvokeFunc(() =>
                    {
                        label1.Text = string.Format("Download of {0} failed", missionName);

                        container.btnStop.Enabled = true;
                    });
                }

                if (modInfo != null && (down == null || down.IsComplete == true))
                {
                    if (Utils.VerifySpringInstalled())
                    {
                        var spring = new Spring(Program.SpringPaths);
                        spring.StartGame(Program.TasClient,
                                         null,
                                         null,
                                         modInfo.MissionScript, Program.Conf.UseSafeMode, Program.Conf.UseMtEngine);

                        var cs = new ContentService() { Proxy = null };
                        cs.NotifyMissionRunAsync(Program.Conf.LobbyPlayerName, missionName);
                    }
                    Program.MainWindow.InvokeFunc(() => Program.NotifySection.RemoveBar(this));
                }
            });
        }
    }
}
