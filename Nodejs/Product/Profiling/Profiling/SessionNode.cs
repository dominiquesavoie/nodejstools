﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.NodejsTools;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;

namespace Microsoft.NodejsTools.Profiling {
    /// <summary>
    /// Represents an individual profiling session.  We have nodes:
    ///     0  - the configuration for what to profile
    ///     1  - a folder, sessions that have been run
    ///     2+ - the sessions themselves.
    ///     
    /// This looks like:
    /// Root
    ///     Target
    ///     Sessions
    ///         Session #1
    ///         Session #2
    /// </summary>
    class SessionNode : BaseHierarchyNode, IVsHierarchyDeleteHandler, IVsPersistHierarchyItem {
        private string _filename;
        private uint _docCookie;
        internal bool _isDirty, _neverSaved;
        private bool _isReportsExpanded;
        private readonly SessionsNode _parent;
        private ProfilingTarget _target;
        private AutomationSession _automationSession;
        internal readonly uint ItemId;

        //private const int ConfigItemId = 0;
        private const int ReportsItemId = 1;
        internal const int StartingReportId = 2;

        public SessionNode(SessionsNode parent, ProfilingTarget target, string filename) {
            _parent = parent;
            _target = target;
            _filename = filename;

            // Register this with the running document table.  This will prompt for save when the file is dirty and
            // by responding to GetProperty for VSHPROPID_ItemDocCookie we will support Ctrl-S when one of our
            // files is dirty.
            // http://msdn.microsoft.com/en-us/library/bb164600(VS.80).aspx
            IVsRunningDocumentTable rdt = NodeProfilingPackage.GetGlobalService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            uint cookie;
            IntPtr punkDocData = Marshal.GetIUnknownForObject(this);
            try {
                ErrorHandler.ThrowOnFailure(rdt.RegisterAndLockDocument((uint)(_VSRDTFLAGS.RDT_VirtualDocument | _VSRDTFLAGS.RDT_EditLock | _VSRDTFLAGS.RDT_CanBuildFromMemory), filename, this, VSConstants.VSITEMID_ROOT, punkDocData, out cookie));
            } finally {
                if (punkDocData != IntPtr.Zero) {
                    Marshal.Release(punkDocData);
                }
            }
            _docCookie = cookie;

            ItemId = parent._sessionsCollection.Add(this);
        }

        public INodeProfileSession GetAutomationObject() {
            if (_automationSession == null) {
                _automationSession = new AutomationSession(this);
            }
            return _automationSession;
        }

        public SortedDictionary<int, Report> Reports {
            get {
                if (_target.Reports == null) {
                    _target.Reports = new Reports();
                }
                if (_target.Reports.AllReports == null) {
                    _target.Reports.AllReports = new SortedDictionary<int, Report>();
                }
                return _target.Reports.AllReports;
            }
        }

        public string Caption {
            get {
                string name = Name;
                if (_isDirty) {
                    return name + " *";
                }
                return name;
            }
        }

        public ProfilingTarget Target {
            get {
                return _target;
            }
        }

        public override int SetProperty(uint itemid, int propid, object var) {
            var prop = (__VSHPROPID)propid;
            switch (prop) {
                case __VSHPROPID.VSHPROPID_Expanded:
                    if (itemid == ReportsItemId) {
                        _isReportsExpanded = Convert.ToBoolean(var);
                        break;
                    }
                    break;
            }

            return base.SetProperty(itemid, propid, var);
        }

        public override int GetProperty(uint itemid, int propid, out object pvar) {
            // GetProperty is called many many times for this particular property
            pvar = null;
            var prop = (__VSHPROPID)propid;
            switch (prop) {
                case __VSHPROPID.VSHPROPID_Parent:
                    if (itemid == ReportsItemId) {
                        pvar = VSConstants.VSITEMID_ROOT;
                    } else if (IsReportItem(itemid)) {
                        pvar = ReportsItemId;
                    }
                    break;

                case __VSHPROPID.VSHPROPID_FirstChild:
                    if (itemid == VSConstants.VSITEMID_ROOT) {
                        pvar = ReportsItemId;
                    } else if (itemid == ReportsItemId && Reports.Count > 0) {
                        pvar = Reports.First().Key;
                    } else {
                        pvar = VSConstants.VSITEMID_NIL;
                    }
                    break;

                case __VSHPROPID.VSHPROPID_NextSibling:
                    pvar = VSConstants.VSITEMID_NIL;
                    if (IsReportItem(itemid)) {
                        var items = Reports.Keys.ToArray();
                        for (int i = 0; i < items.Length; i++) {
                            if (items[i] > (int)itemid) {
                                pvar = itemid + 1;
                                break;
                            }
                        }
                    }
                    break;
                case __VSHPROPID.VSHPROPID_ItemDocCookie:
                    if (itemid == VSConstants.VSITEMID_ROOT) {
                        pvar = (int)_docCookie;
                    }
                    break;

                case __VSHPROPID.VSHPROPID_Expandable:
                    if (itemid == VSConstants.VSITEMID_ROOT) {
                        pvar = true;
                    } else if (itemid == ReportsItemId && Reports.Count > 0) {
                        pvar = true;
                    } else {
                        pvar = false;
                    }
                    break;

                case __VSHPROPID.VSHPROPID_ExpandByDefault:
                    pvar = true;
                    break;

                case __VSHPROPID.VSHPROPID_IconImgList:
                case __VSHPROPID.VSHPROPID_OpenFolderIconHandle:
                    pvar = (int)SessionsNode._imageList.Handle;
                    break;

                case __VSHPROPID.VSHPROPID_IconIndex:
                case __VSHPROPID.VSHPROPID_OpenFolderIconIndex:
                    if (itemid == ReportsItemId) {
                        if (_isReportsExpanded) {
                            pvar = (int)TreeViewIconIndex.OpenFolder;
                        } else {
                            pvar = (int)TreeViewIconIndex.CloseFolder;
                        }
                    } else if (IsReportItem(itemid)) {
                        pvar = (int)TreeViewIconIndex.GreenNotebook;
                    }
                    break;

                case __VSHPROPID.VSHPROPID_Caption:
                    if (itemid == VSConstants.VSITEMID_ROOT) {
                        pvar = Caption;
                    } else if (itemid == ReportsItemId) {
                        pvar = "Reports";
                    } else if (IsReportItem(itemid)) {
                        pvar = Path.GetFileNameWithoutExtension(GetReport(itemid).Filename);
                    }
                    break;

                case __VSHPROPID.VSHPROPID_ParentHierarchy:
                    if (itemid == VSConstants.VSITEMID_ROOT) {
                        pvar = _parent as IVsHierarchy;
                    }
                    break;
            }

            if (pvar != null)
                return VSConstants.S_OK;

            return VSConstants.DISP_E_MEMBERNOTFOUND;
        }

        public override int ExecCommand(uint itemid, ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            if (pguidCmdGroup == VsMenus.guidVsUIHierarchyWindowCmds) {
                switch ((VSConstants.VsUIHierarchyWindowCmdIds)nCmdID) {
                    case VSConstants.VsUIHierarchyWindowCmdIds.UIHWCMDID_DoubleClick:
                    case VSConstants.VsUIHierarchyWindowCmdIds.UIHWCMDID_EnterKey:
                        if (itemid == VSConstants.VSITEMID_ROOT) {
                            OpenTargetProperties();

                            // S_FALSE: don't process the double click to expand the item
                            return VSConstants.S_FALSE;
                        } else if (IsReportItem(itemid)) {
                            OpenProfile(itemid);
                        }

                        return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
                    case VSConstants.VsUIHierarchyWindowCmdIds.UIHWCMDID_RightClick:
                        int? ctxMenu = null;

                        if (itemid == VSConstants.VSITEMID_ROOT) {
                            ctxMenu = (int)PkgCmdIDList.menuIdPerfContext;
                        } else if (itemid == ReportsItemId) {
                            ctxMenu = (int)PkgCmdIDList.menuIdPerfReportsContext;
                        } else if (IsReportItem(itemid)) {
                            ctxMenu = (int)PkgCmdIDList.menuIdPerfSingleReportContext;
                        }

                        if (ctxMenu != null) {
                            var uishell = (IVsUIShell)NodePackage.GetGlobalService(typeof(SVsUIShell));
                            if (uishell != null) {
                                var pt = System.Windows.Forms.Cursor.Position;
                                var pnts = new[] { new POINTS { x = (short)pt.X, y = (short)pt.Y } };
                                var guid = GuidList.guidNodeProfilingCmdSet;
                                int hr = uishell.ShowContextMenu(
                                    0,
                                    ref guid,
                                    ctxMenu.Value,
                                    pnts,
                                    new ContextCommandTarget(this, itemid));

                                ErrorHandler.ThrowOnFailure(hr);
                            }
                        }

                        break;
                }
            }

            return base.ExecCommand(itemid, ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        internal ProfilingTarget OpenTargetProperties() {
            var targetView = new ProfilingTargetView(_target);
            var dialog = new LaunchProfiling(targetView);
            var res = dialog.ShowModal() ?? false;
            if (res && targetView.IsValid) {
                var target = targetView.GetTarget();
                if (target != null && !ProfilingTarget.IsSame(target, _target)) {
                    _target = target;
                    MarkDirty();
                    return _target;
                }
            }
            return null;
        }

        private void OpenProfile(uint itemid) {
            var item = GetReport(itemid);

            if (!File.Exists(item.Filename)) {
                MessageBox.Show(String.Format("Performance report no longer exists: {0}", item.Filename), Resources.NodejsToolsForVS);
            } else {
                var dte = (EnvDTE.DTE)NodeProfilingPackage.GetGlobalService(typeof(EnvDTE.DTE));
                dte.ItemOperations.OpenFile(item.Filename);
            }
        }

        class ContextCommandTarget : IOleCommandTarget {
            private readonly SessionNode _node;
            private readonly uint _itemid;

            public ContextCommandTarget(SessionNode node, uint itemid) {
                _node = node;
                _itemid = itemid;
            }

            #region IOleCommandTarget Members

            public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
                if (pguidCmdGroup == GuidList.guidNodeProfilingCmdSet) {
                    switch (nCmdID) {
                        case PkgCmdIDList.cmdidOpenReport:
                            _node.OpenProfile(_itemid);
                            return VSConstants.S_OK;

                        case PkgCmdIDList.cmdidPerfCtxSetAsCurrent:
                            _node._parent.SetActiveSession(_node);
                            return VSConstants.S_OK;

                        case PkgCmdIDList.cmdidPerfCtxStartProfiling:
                            _node.StartProfiling();
                            return VSConstants.S_OK;

                        case PkgCmdIDList.cmdidReportsCompareReports: {
                            CompareReportsView compareView;
                            if (_node.IsReportItem(_itemid)) {
                                var report = _node.GetReport(_itemid);
                                compareView = new CompareReportsView(report.Filename);
                            } else {
                                compareView = new CompareReportsView();
                            }

                            var dialog = new CompareReportsWindow(compareView);
                            var res = dialog.ShowModal() ?? false;
                            if (res && compareView.IsValid) {
                                IVsUIShellOpenDocument sod = NodeProfilingPackage.GetGlobalService(typeof(SVsUIShellOpenDocument)) as IVsUIShellOpenDocument;
                                Debug.Assert(sod != null);
                                Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame frame = null;
                                Guid guid = new Guid("{9C710F59-984F-4B83-B781-B6356C363B96}"); // performance diff guid
                                Guid guidNull = Guid.Empty;

                                sod.OpenSpecificEditor(
                                    (uint)(_VSRDTFLAGS.RDT_CantSave | _VSRDTFLAGS.RDT_DontAddToMRU | _VSRDTFLAGS.RDT_NonCreatable | _VSRDTFLAGS.RDT_NoLock),
                                    compareView.GetComparisonUri(),
                                    ref guid,
                                    null,
                                    ref guidNull,
                                    "Performance Comparison",
                                    _node,
                                    _itemid,
                                    IntPtr.Zero,
                                    null,
                                    out frame
                                );

                                if (frame != null) {
                                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
                                }
                            }
                            return VSConstants.S_OK;
                        }
                        case PkgCmdIDList.cmdidReportsAddReport: {
                            var dialog = new OpenFileDialog();
                            dialog.Filter = NodeProfilingPackage.PerformanceFileFilter;
                            dialog.CheckFileExists = true;
                            var res = dialog.ShowDialog() ?? false;
                            if (res) {
                                _node.AddProfile(dialog.FileName);
                            }
                            return VSConstants.S_OK;
                        }
                    }
                } else if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97) {
                    switch((VSConstants.VSStd97CmdID)nCmdID) {
                        case VSConstants.VSStd97CmdID.PropSheetOrProperties:
                            _node.OpenTargetProperties();
                            return VSConstants.S_OK;

                    }
                }
                return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) {
                return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            #endregion
        }

        internal void MarkDirty() {
            _isDirty = true;
            OnPropertyChanged(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Caption, 0);
        }

        public override int QueryStatusCommand(uint itemid, ref Guid pguidCmdGroup, uint cCmds, VisualStudio.OLE.Interop.OLECMD[] prgCmds, IntPtr pCmdText) {
            return base.QueryStatusCommand(itemid, ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }
        
        private bool IsReportItem(uint itemid) {
            return itemid >= StartingReportId && Reports.ContainsKey((int)itemid);
        }

        private Report GetReport(uint itemid) {
            return Reports[(int)itemid];
        }

        public void AddProfile(string filename) {
            if (_target.Reports == null) {
                _target.Reports = new Reports(new[] { new Report(filename) });
            } else {
                if (_target.Reports.Report == null) {
                    _target.Reports.Report = new Report[0];
                }

                var reportIds = Reports.Keys.ToArray();
                uint prevSibling, newId;
                if (reportIds.Length > 0) {
                    prevSibling = (uint)reportIds[reportIds.Length - 1];
                    newId = prevSibling + 1;
                } else {
                    prevSibling = VSConstants.VSITEMID_NIL;
                    newId = StartingReportId;
                }

                Reports[(int)newId] = new Report(filename);

                OnItemAdded(
                    ReportsItemId,
                    prevSibling, 
                    newId
                );
            }

            MarkDirty();
        }

        public void Save(VSSAVEFLAGS flags, out int pfCanceled) {
            pfCanceled = 0;
            switch (flags) {
                case VSSAVEFLAGS.VSSAVE_Save:
                    if (_neverSaved) {
                        goto case VSSAVEFLAGS.VSSAVE_SaveAs;
                    }
                    Save(_filename);
                    break;
                case VSSAVEFLAGS.VSSAVE_SaveAs:
                case VSSAVEFLAGS.VSSAVE_SaveCopyAs:
                    SaveFileDialog saveDialog = new SaveFileDialog();
                    saveDialog.FileName = _filename;                    
                    if (saveDialog.ShowDialog() == true) {
                        Save(saveDialog.FileName);
                        _neverSaved = false;
                    } else {
                        pfCanceled = 1;
                    }
                    break;
            }
        }

        internal void Removed() {
            IVsRunningDocumentTable rdt = NodeProfilingPackage.GetGlobalService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            ErrorHandler.ThrowOnFailure(rdt.UnlockDocument((uint)_VSRDTFLAGS.RDT_EditLock, _docCookie));
        }

        #region IVsHierarchyDeleteHandler Members

        public int DeleteItem(uint dwDelItemOp, uint itemid) {
            Debug.Assert(_target.Reports != null && _target.Reports.Report != null && _target.Reports.Report.Length > 0);

            var report = GetReport(itemid);
            Reports.Remove((int)itemid);
            
            OnItemDeleted(itemid);
            OnInvalidateItems(ReportsItemId);

            if (File.Exists(report.Filename) && dwDelItemOp == (uint)__VSDELETEITEMOPERATION.DELITEMOP_DeleteFromStorage) {
                // close the file if it's open before deleting it...
                var dte = (EnvDTE.DTE)NodeProfilingPackage.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte.ItemOperations.IsFileOpen(report.Filename)) {
                    var doc = dte.Documents.Item(report.Filename);
                    doc.Close();
                }

                File.Delete(report.Filename);
            }

            return VSConstants.S_OK;
        }

        public int QueryDeleteItem(uint dwDelItemOp, uint itemid, out int pfCanDelete) {
            if (IsReportItem(itemid)) {
                pfCanDelete = 1;
                return VSConstants.S_OK;
            }
            pfCanDelete = 0;
            return VSConstants.S_OK;
        }

        #endregion

        internal void StartProfiling(bool openReport = true) {
            NodeProfilingPackage.Instance.StartProfiling(_target, this, openReport);
        }

        public void Save(string filename = null) {
            if (filename == null) {
                filename = _filename;
            }

            using (var stream = new FileStream(filename, FileMode.Create)) {
                ProfilingTarget.Serializer.Serialize(
                    stream,
                    _target
                );
                _isDirty = false;
                stream.Close();
                OnPropertyChanged(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Caption, 0);
            }
        }

        public string Filename { get { return _filename; } }

        public string Name {
            get {
                return Path.GetFileNameWithoutExtension(_filename);
            }
        }

        public bool IsSaved {
            get {
                return !_isDirty && !_neverSaved;
            }
        }

        #region IVsPersistHierarchyItem Members

        public int IsItemDirty(uint itemid, IntPtr punkDocData, out int pfDirty) {
            if (itemid == VSConstants.VSITEMID_ROOT) {
                pfDirty = _isDirty ? 1 : 0;
                return VSConstants.S_OK;
            } else {
                pfDirty = 0;
                return VSConstants.E_FAIL;
            }
        }

        public int SaveItem(VSSAVEFLAGS dwSave, string pszSilentSaveAsName, uint itemid, IntPtr punkDocData, out int pfCanceled) {
            if (itemid == VSConstants.VSITEMID_ROOT) {
                Save(dwSave, out pfCanceled);
                return VSConstants.S_OK;
            }
            pfCanceled = 0;
            return VSConstants.E_FAIL;
        }

        #endregion
    }
}