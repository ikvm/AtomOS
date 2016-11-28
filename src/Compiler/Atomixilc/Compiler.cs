﻿using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Atomixilc.IL;
using Atomixilc.Machine;
using Atomixilc.Attributes;
using Atomixilc.Machine.x86;

namespace Atomixilc
{
    internal class Compiler
    {
        Options Config;
        Dictionary<ILCode, MSIL> ILCodes;

        Dictionary<MethodBase, string> Plugs;
        Dictionary<string, MethodBase> Labels;

        Queue<object> ScanQ;
        HashSet<object> FinishedQ;
        HashSet<MethodBase> Virtual;

        internal Compiler(Options aCompilerOptions)
        {
            Config = aCompilerOptions;

            PrepareEnvironment();
        }

        internal void PrepareEnvironment()
        {
            var ExecutingAssembly = Assembly.GetExecutingAssembly();

            ILCodes = new Dictionary<ILCode, MSIL>();
            Plugs = new Dictionary<MethodBase, string>();
            Labels = new Dictionary<string, MethodBase>();
            ScanQ = new Queue<object>();
            FinishedQ = new HashSet<object>();
            Virtual = new HashSet<MethodBase>();

            var types = ExecutingAssembly.GetTypes();
            foreach (var type in types)
            {
                var attributes = type.GetCustomAttributes<ILImplAttribute>();
                foreach (var attrib in attributes)
                {
                    ILCodes.Add(attrib.OpCode, (MSIL)Activator.CreateInstance(type, this));
                }
            }
        }

        internal void Execute()
        {
            Type Entrypoint;
            ScanInputAssembly(out Entrypoint);

            if (Entrypoint == null)
                throw new Exception("No input entrypoint found");

            var main = Entrypoint.GetMethod("main");
            if (main == null)
                throw new Exception("No main function found");

            ScanQ.Clear();
            Virtual.Clear();
            FinishedQ.Clear();

            ScanQ.Enqueue(main);
            while(ScanQ.Count != 0)
            {
                var ScanObject = ScanQ.Dequeue();

                if (FinishedQ.Contains(ScanObject))
                    continue;

                var method = ScanObject as MethodBase;
                if (method != null)
                {
                    ScanMethod(method);
                    continue;
                }

                var type = ScanObject as Type;
                if (type != null)
                {
                    ScanType(type);
                    continue;
                }

                var field = ScanObject as FieldInfo;
                if (field != null)
                {
                    continue;
                }

                throw new Exception(string.Format("Invalid Object in Queue of type '{0}'", ScanObject.GetType()));
            }
        }

        internal void ScanInputAssembly(out Type Entrypoint)
        {
            var InputAssembly = Assembly.LoadFile(Config.InputFiles[0]);

            var types = InputAssembly.GetTypes();

            Entrypoint = null;
            Plugs.Clear();
            Labels.Clear();
            foreach (var type in types)
            {
                var entrypointattrib = type.GetCustomAttribute<EntrypointAttribute>();
                if (entrypointattrib != null && entrypointattrib.Platform == Config.TargetPlatform)
                {
                    if (Entrypoint != null)
                        throw new Exception("Multiple Entrypoint with same target platform");
                    Entrypoint = type;
                }

                var methods = type.GetMethods();
                foreach (var method in methods)
                {
                    var plugattrib = method.GetCustomAttribute<PlugAttribute>();
                    if (plugattrib != null && plugattrib.Platform == Config.TargetPlatform)
                    {
                        if (Plugs.ContainsValue(plugattrib.TargetLabel))
                            throw new Exception(string.Format("Multiple plugs with same target label '{0}'", plugattrib.TargetLabel));
                        Plugs.Add(method, plugattrib.TargetLabel);
                    }

                    var labelattrib = method.GetCustomAttribute<LabelAttribute>();
                    if (labelattrib != null)
                    {
                        if (Labels.ContainsKey(labelattrib.RefLabel))
                            throw new Exception(string.Format("Multiple labels with same Ref label '{0}'", labelattrib.RefLabel));
                        Labels.Add(labelattrib.RefLabel, method);
                    }
                }
            }
        }

        internal void ScanType(Type type)
        {
            if (type.BaseType != null)
                ScanQ.Enqueue(type);

            var constructors = type.GetConstructors();
            foreach (var ctor in constructors)
            {
                if (ctor.DeclaringType != type)
                    continue;
                ScanQ.Enqueue(ctor);
            }

            var methods = type.GetMethods();
            foreach(var method in methods)
            {
                var basedefination = method.GetBaseDefinition();

                if (Virtual.Contains(method) ||
                    !basedefination.IsAbstract ||
                    method.DeclaringType.IsAbstract ||
                    basedefination.DeclaringType == method.DeclaringType)
                    continue;

                Virtual.Add(method);
                ScanQ.Enqueue(method);
            }

            FinishedQ.Add(type);
        }

        internal void ScanMethod(MethodBase method)
        {
            if (method.GetCustomAttribute<AssemblyAttribute>() != null)
                ProcessAssemblyMethod(method);
            else if (method.GetCustomAttribute<DllImportAttribute>() != null)
                ProcessExternMethod(method);
            else
                ProcessMethod(method);

            FinishedQ.Add(method);
        }

        internal void ProcessAssemblyMethod(MethodBase method)
        {
            var attrib = method.GetCustomAttribute<AssemblyAttribute>();
            if (attrib == null)
                throw new Exception("Invalid call to ProcessAssemblyMethod");

            var block = new FunctionalBlock(method.FullName(), Config.TargetPlatform, CallingConvention.StdCall);

            Instruction.Block = block;

            if (attrib.CalliHeader)
                EmitHeader(block, method);

            try
            {
                method.Invoke(null, new object[method.GetParameters().Length]);
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("Exception occured while invoking assembly function '{0}' => {1}", method.FullName(), e.Message));
            }

            if (attrib.CalliHeader)
                EmitFooter(block, method);

            Instruction.Block = null;
        }

        internal void ProcessExternMethod(MethodBase method)
        {
            var attrib = method.GetCustomAttribute<DllImportAttribute>();
            if (attrib == null)
                throw new Exception("Invalid call to ProcessExternMethod");

        }

        internal void ProcessMethod(MethodBase method)
        {

        }

        internal void EmitHeader(FunctionalBlock block, MethodBase method)
        {
            switch(block.CallingConvention)
            {
                case CallingConvention.StdCall:
                    {
                        switch(block.Platform)
                        {
                            case Architecture.x86:
                                {
                                    new Push { DestinationReg = Register.EBP };
                                    new Mov { DestinationReg = Register.EBP, SourceReg = Register.ESP };
                                }
                                break;
                            default:
                                throw new Exception(string.Format("Unsupported Platform method '{0}'", method.FullName()));
                        }
                    }
                    break;
                default:
                    throw new Exception(string.Format("Unsupported CallingConvention used in method '{0}'", method.FullName()));
            }
        }

        internal void EmitFooter(FunctionalBlock block, MethodBase method)
        {
            var paramsSize = method.GetParameters().Sum(arg => GetTypeSize(arg.ParameterType));

            if (paramsSize > 255) throw new Exception(string.Format("Too large stack frame for parameters '{0}'", method.FullName()));

            switch (block.CallingConvention)
            {
                case CallingConvention.StdCall:
                    {
                        switch (block.Platform)
                        {
                            case Architecture.x86:
                                {
                                    new Leave { };
                                    new Ret { Offset = (byte)paramsSize };
                                }
                                break;
                            default:
                                throw new Exception(string.Format("Unsupported Platform method '{0}'", method.FullName()));
                        }
                    }
                    break;
                default:
                    throw new Exception(string.Format("Unsupported CallingConvention used in method '{0}'", method.FullName()));
            }
        }

        internal int GetTypeSize(Type type)
        {
            if (type == typeof(void))
                return 0;

            if ((type == typeof(byte))
                || (type == typeof(sbyte))
                || (type == typeof(bool)))
                return 1;

            if ((type == typeof(char))
                || (type == typeof(short))
                || (type == typeof(ushort)))
                return 2;

            if ((type == typeof(int))
                || (type == typeof(uint))
                || (type == typeof(float)))
                return 4;

            if ((type == typeof(long))
                || (type == typeof(ulong))
                || (type == typeof(double)))
                return 8;

            if (type == typeof(decimal))
                return 16;

            if ((type == typeof(IntPtr))
                || (type == typeof(UIntPtr))
                || (type.IsByRef)
                || (!type.IsValueType && type.IsClass)
                || (!string.IsNullOrEmpty(type.FullName) && type.FullName.EndsWith("*")))
            {
                switch(Config.TargetPlatform)
                {
                    case Architecture.x86: return 4;
                    case Architecture.x64: return 8;
                    default: throw new Exception(string.Format("GetTypeSize Unknown Platform '{0}'", Config.TargetPlatform));
                }
            }

            if (type.IsEnum)
                return GetTypeSize(type.GetField("value__").FieldType);

            if (type.IsValueType)
            {
                var size = type.GetFields().Sum(field => GetTypeSize(field.FieldType));
                var attrib = type.StructLayoutAttribute;
                if (attrib != null && size != attrib.Size)
                {
                    size = Math.Max(size, attrib.Size);
                    Verbose.Warning("GetTypeSize of type '{0}' mismatch. taking size: '{1}'", type, size);
                }
                return size;
            }

            throw new Exception(string.Format("GetTypeSize of Unhandled type '{0}'", type));
        }
    }
}
