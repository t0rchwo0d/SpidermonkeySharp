﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Spidermonkey.Managed {
    public class NativeToManagedProxy : IDisposable {
        public readonly Delegate ManagedMethod;
        public readonly JSNative WrappedMethod;
        public readonly uint ArgumentCount;
        public readonly ParameterInfo[] ArgumentInfo;
        private readonly GCHandle Pin;

        public NativeToManagedProxy (Delegate managedMethod) {
            ManagedMethod = managedMethod;

            var invoke = GetType().GetMethod("Invoke", BindingFlags.NonPublic | BindingFlags.Instance);
            WrappedMethod = (JSNative)Delegate.CreateDelegate(typeof(JSNative), this, invoke, true);

            ArgumentInfo = managedMethod.Method.GetParameters();
            ArgumentCount = (uint)ArgumentInfo.Length;

            Pin = GCHandle.Alloc(WrappedMethod);
        }

        private static object NativeToManaged (JSContextPtr cx, JS.Value value) {
            return value.ToManaged(cx);
        }

        private static JS.Value ManagedToNative (JSContextPtr cx, object value) {
            if (value == null) {
                return JS.Value.Null;
            }

            var s = value as string;
            if (s != null) {
                var pString = JSAPI.NewStringCopy(cx, s);
                return new JS.Value(pString);
            }

            return (JS.Value)Activator.CreateInstance(typeof(JS.Value), value);
        }

        private static unsafe void Throw (JSContextPtr cx, params object[] errorArguments) {
            var jsErrorArgs = new JS.ValueArray((uint)errorArguments.Length);
            for (int i = 0; i < errorArguments.Length; i++)
                jsErrorArgs.Elements[i] = ManagedToNative(cx, errorArguments[i]);

            JS.ValueArrayPtr vaPtr = jsErrorArgs;
            var errorRoot = new Rooted<JS.Value>(
                cx, new JS.Value(JSAPI.NewError(cx, ref vaPtr))
            );
            JSAPI.SetPendingException(cx, errorRoot);
        }

        private bool Invoke (JSContextPtr cx, uint argc, JSCallArgumentsPtr args) {
            var managedArgs = new object[ArgumentCount];
            for (uint i = 0, l = Math.Min(ArgumentCount, argc); i < l; i++)
                managedArgs[i] = NativeToManaged(cx, args[i]);

            try {
                var managedResult = ManagedMethod.DynamicInvoke(managedArgs);

                args.Result = ManagedToNative(cx, managedResult);
                return true;
            } catch (Exception exc) {
                Throw(cx, exc.Message);

                return false;
            }
        }

        public void Dispose () {
            Pin.Free();
        }
    }
}
