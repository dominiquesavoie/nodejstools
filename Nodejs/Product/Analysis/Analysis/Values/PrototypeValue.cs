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


using Microsoft.NodejsTools.Analysis.Analyzer;
namespace Microsoft.NodejsTools.Analysis.Values {
    class PrototypeValue : ObjectValue {
        private readonly InstanceValue _instance;

        public PrototypeValue(ProjectEntry projectEntry, InstanceValue instance, string description = null)
            : base(projectEntry, description: description) {
            _instance = instance;
            projectEntry.Analyzer.AnalysisValueCreated(typeof(PrototypeValue));
        }

        public override void SetMember(Parsing.Node node, AnalysisUnit unit, string name, IAnalysisSet value) {
            foreach (var obj in value) {
                // function Class() {
                //     this.abc = 42;
                // }
                //   
                // Class.prototype.foo = function(fn) {
                //     var x = this.abc;
                // }
                // this now includes us.

                UserFunctionValue userFunc = obj as UserFunctionValue;
                if (userFunc != null) {
                    var env = (FunctionEnvironmentRecord)(userFunc.AnalysisUnit._env);

                    env._this.AddTypes(unit, _instance.SelfSet);
                }
            }
            base.SetMember(node, unit, name, value);
        }
    }
}
