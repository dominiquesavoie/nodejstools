// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Windows.Input;
using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;

namespace TestUtilities
{
    public interface IVisualStudioInstance : IDisposable
    {
        void Type(Key key);

        void Type(string p);

        void ControlC();
        void ControlV();

        void ControlX();

        void CheckMessageBox(params string[] text);
        void CheckMessageBox(MessageBoxButton button, params string[] text);

        ITreeNode WaitForItem(params string[] items);

        ITreeNode FindItem(params string[] items);

        IEditor OpenItem(string project, params string[] path);

        ITreeNode WaitForItemRemoved(params string[] path);

        void WaitForOutputWindowText(string name, string containsText, int timeout = 5000);


        void Sleep(int ms);

        void ExecuteCommand(string command);

        string SolutionFilename { get; }
        string SolutionDirectory { get; }

        IntPtr WaitForDialog();

        void WaitForDialogDismissed();

        void AssertFileExists(params string[] path);

        void AssertFileDoesntExist(params string[] path);

        void AssertFolderExists(params string[] path);

        void AssertFolderDoesntExist(params string[] path);

        void AssertFileExistsWithContent(string content, params string[] path);

        void CloseActiveWindow(vsSaveChanges save);

        IntPtr OpenDialogWithDteExecuteCommand(string commandName, string commandArgs = "");

        void SelectSolutionNode();

        Project GetProject(string projectName);

        void SelectProject(Project project);

        IEditor GetDocument(string filename);

        IAddExistingItem AddExistingItem();

        IAddNewItem AddNewItem();

        IOverwriteFile WaitForOverwriteFileDialog();

        void WaitForMode(dbgDebugMode dbgDebugMode);

        List<IVsTaskItem> WaitForErrorListItems(int expectedCount);

        DTE Dte { get; }

        void OnDispose(Action action);
        void PressAndRelease(Key key, params Key[] modifier);
    }
}

