﻿/***

   Copyright (C) 2019. rollrat. All Rights Reserved.
   
   Author: HyunJun Jeong

***/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace kakaotalk_analyzer.Core.Console.CoreUtils
{
    /// <summary>
    /// Internal 콘솔 옵션입니다.
    /// </summary>
    public class InternalConsoleOption : IConsoleOption
    {
        [CommandLine("--help", CommandType.OPTION, Default = true)]
        public bool Help;

        [CommandLine("-e", CommandType.ARGUMENTS, ArgumentsCount = 1,
            Info = "Enumerate metadata from implements classes.")]
        public string[] Enumerate;

        [CommandLine("-F", CommandType.OPTION, Info = "Set enumerate based on forms, windows")]
        public bool EnumerateWithForms;
        [CommandLine("-P", CommandType.OPTION, Info = "Set enumerate with private members.")]
        public bool EnumerateWithPrivate;
        [CommandLine("-I", CommandType.OPTION, Info = "Set enumerate based on instances")]
        public bool EnumerateWithInstances;
        [CommandLine("-S", CommandType.OPTION, Info = "Set enumerate with static members.")]
        public bool EnumerateWithStatic;
        [CommandLine("-E", CommandType.OPTION, Info = "Set enumerate with methods.")]
        public bool EnumerateWithMethod;

        [CommandLine("--get", CommandType.ARGUMENTS, ArgumentsCount = 1, Help = "--get \"<path1>.<path2> ...\" ",
            Info = "Get memory data using metadata path information.")]
        public string[] Get;
        //[CommandLine("-P", CommandType.OPTION)]
        //public bool GetWithProperty;

        [CommandLine("--set", CommandType.ARGUMENTS, ArgumentsCount = 2, Pipe = true,
            Info = "Set to memory data using metadata path information.")]
        public string[] Set;

        [CommandLine("--call", CommandType.ARGUMENTS, ArgumentsCount = 2, Pipe = true,
            Info = "Call method using metadata path information and parameters.")]
        public string[] Call;
        [CommandLine("-R", CommandType.OPTION,
            Info = "Save return result after calling.")]
        public bool CallWithReturn;
    }

    /// <summary>
    /// 파일의 내용을 가져옵니다.
    /// </summary>
    public class InternalConsole : IConsole
    {
        static object latest_target = null;

        /// <summary>
        /// Internal 콘솔 리다이렉트
        /// </summary>
        static bool Redirect(string[] arguments, string contents)
        {
            arguments = CommandLineUtil.SplitCombinedOptions(arguments);
            if (CommandLineUtil.AnyArgument(arguments, "-e"))
            {
                arguments = CommandLineUtil.DeleteArgument(arguments, "-e");
                if (!CommandLineUtil.AnyStrings(arguments))
                    arguments = CommandLineUtil.PushFront(arguments, "");
            }
            arguments = CommandLineUtil.InsertWeirdArguments<InternalConsoleOption>(arguments, true, "-e");
            InternalConsoleOption option = CommandLineParser<InternalConsoleOption>.Parse(arguments);

            if (option.Error)
            {
                Console.Instance.WriteLine(option.ErrorMessage);
                if (option.HelpMessage != null)
                    Console.Instance.WriteLine(option.HelpMessage);
                return false;
            }
            else if (option.Help)
            {
                PrintHelp();
            }
            else if (option.Enumerate != null)
            {
                ProcessEnumerate(option.Enumerate, option.EnumerateWithForms, option.EnumerateWithPrivate,
                    option.EnumerateWithInstances, option.EnumerateWithStatic, option.EnumerateWithMethod);
            }
            else if (option.Get != null)
            {
                ProcessGet(option.Get, option.EnumerateWithForms, option.EnumerateWithInstances, option.EnumerateWithPrivate);
            }
            else if (option.Set != null)
            {
                ProcessSet(option.Set, option.EnumerateWithForms, option.EnumerateWithInstances);
            }
            else if (option.Call != null)
            {
                ProcessCall(option.Call, option.EnumerateWithForms, option.EnumerateWithInstances, option.CallWithReturn);
            }

            return true;
        }

        bool IConsole.Redirect(string[] arguments, string contents)
        {
            return Redirect(arguments, contents);
        }

        static void PrintHelp()
        {
            Console.Instance.WriteLine(
                "Internal Console\r\n" +
                "\r\n" +
                " -e [-F | -P | -I | -S] <path> : Enumerate method."
                );

            var builder = new StringBuilder();
            CommandLineParser<InternalConsoleOption>.GetFields().ToList().ForEach(
                x =>
                {
                    if (!string.IsNullOrEmpty(x.Value.Item2.Help))
                        builder.Append($" {x.Key} ({x.Value.Item2.Help}) : {x.Value.Item2.Info} [{x.Value.Item1}]\r\n");
                    else
                        builder.Append($" {x.Key} : {x.Value.Item2.Info} [{x.Value.Item1}]\r\n");
                });
            Console.Instance.WriteLine(builder.ToString());
        }

        static private bool Initialized = false;
        //static public Dictionary<string, object> instances = new Dictionary<string, object>();
        static public Func<Task<object[]>> get_windows;
        static public Func<string, Task<object>> get_window;

        /// <summary>
        /// 특정 클래스의 내용을 나열합니다.
        /// </summary>
        /// <param name="e_private"></param>
        /// <param name="e_instance"></param>
        /// <param name="e_static"></param>
        static void ProcessEnumerate(string[] args, bool e_form, bool e_private, bool e_instance, bool e_static, bool e_method)
        {
            var split = args[0].Split('.');

            bool default_out = false;

            if (split[0] == "" && split.Length == 1)
            {
                default_out = true;
            }

            if (!(e_form || e_instance))
            {
                if (InstanceMonitor.Instances.ContainsKey(split[0]))
                    e_instance = true;
                else
                    e_form = true;
            }

            var list = new List<string>();

            var option = Internal.CommonBinding;
            if (e_private)
                option = Internal.DefaultBinding;
            if (e_static)
                option |= BindingFlags.Static;

            if (default_out)
            {
                if (e_form)
                {
                    foreach (var f in get_windows().Result)
                        list.Add(f.GetType().Name);
                }
                else if (e_instance)
                {
                    foreach (var pair in InstanceMonitor.Instances)
                        list.Add($"{pair.Key.PadRight(18)} [{pair.Value.ToString()}]");
                }
            }
            else
            {
                object target = split[0] == "<latest>" ? latest_target : e_form ? get_window(split[0]).Result : InstanceMonitor.Instances[split[0]];

                if (!e_method)
                {
                    list.AddRange(
                        Internal.enum_recursion(target, split, 1, option)
                        .Select(x => $"{x.Name.PadRight(25)} [{x.FieldType.ToString()}]"));
                }
                else
                {
                    list.AddRange(
                        Internal.enum_methods(target, split, 1, option)
                        .Select(x => $"{x.Name.PadRight(25)} [return:({x.ReturnType.ToString()}), args:({string.Join(", ", x.GetParameters().Select(y => $"{y.Name}: {y.ParameterType.ToString()}"))})]"));
                }
            }

            list.ForEach(x => Console.Instance.WriteLine(x));
        }

        /// <summary>
        /// 특정 변수의 데이터를 가져옵니다.
        /// </summary>
        /// <param name="args"></param>
        static void ProcessGet(string[] args, bool e_form, bool e_instance, bool e_property)
        {
            var split = args[0].Split('.');

            if (!(e_form || e_instance))
            {
                if (InstanceMonitor.Instances.ContainsKey(split[0]))
                    e_instance = true;
                else
                    e_form = true;
            }

            object target = split[0] == "<latest>" ? latest_target : e_form ? get_window(split[0]).Result : InstanceMonitor.Instances[split[0]];
            string result = null;

            result = Monitor.SerializeObject(Internal.get_recursion(target, split, 1));

            Console.Instance.WriteLine(result);
        }

        /// <summary>
        /// 특정 변수에 데이터를 지정합니다.
        /// </summary>
        /// <param name="args"></param>
        /// <param name="e_form"></param>
        /// <param name="e_instance"></param>
        static void ProcessSet(string[] args, bool e_form, bool e_instance)
        {
            var split = args[0].Split('.');

            if (!(e_form || e_instance))
            {
                if (InstanceMonitor.Instances.ContainsKey(split[0]))
                    e_instance = true;
                else
                    e_form = true;
            }

            object target = split[0] == "<latest>" ? latest_target : e_form ? get_window(split[0]).Result : InstanceMonitor.Instances[split[0]];
            Internal.set_recursion(target, split, 1, args[1]);
        }

        /// <summary>
        /// 특정 함수를 호출합니다.
        /// </summary>
        /// <param name="args"></param>
        /// <param name="e_form"></param>
        /// <param name="e_instance"></param>
        static void ProcessCall(string[] args, bool e_form, bool e_instance, bool e_return)
        {
            var split = args[0].Split('.');

            if (!(e_form || e_instance))
            {
                if (InstanceMonitor.Instances.ContainsKey(split[0]))
                    e_instance = true;
                else
                    e_form = true;
            }

            object target = split[0] == "<latest>" ? latest_target : e_form ? get_window(split[0]).Result : InstanceMonitor.Instances[split[0]];
            object[] param = null;

            if (args[1] != "")
            {
                var pis = Internal.get_method_paraminfo(target, split, 1, Internal.DefaultBinding);
                var pst = args[1].Split(',');

                param = new object[pis.Length];
                for (int i = 0; i < pis.Length; i++)
                {
                    try
                    {
                        param[i] = Convert.ChangeType(pst[i], pis[i].ParameterType);
                    }
                    catch (Exception e)
                    {
                        param[i] = JsonConvert.DeserializeObject(pst[i], pis[i].ParameterType);
                    }
                }
            }

            object returns = Internal.call_method(target, split, 1, Internal.DefaultBinding, param);

            if (e_return)
            {
                Console.Instance.WriteLine(Monitor.SerializeObject(latest_target = returns));
            }
        }
    }

    /// <summary>
    /// 오브젝트의 데이터를 가져오거나설정합니다.
    /// </summary>
    public class Internal
    {
        #region Low Level

        /// <summary>
        /// 모든 조건을 포함하는 바인딩 옵션
        /// </summary>
        public const BindingFlags DefaultBinding = BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// 매우 간단한 내용만 보여주는 바인딩 옵션
        /// </summary>
        public const BindingFlags CommonBinding = BindingFlags.Instance | BindingFlags.Public;

        public static List<FieldInfo> get_all_fields(Type t, BindingFlags flags)
        {
            if (t == null)
                return new List<FieldInfo>();

            var list = t.GetFields(flags).ToList();
            list.AddRange(get_all_fields(t.BaseType, flags));
            return list;
        }

        public static List<FieldInfo> enum_recursion(object obj, string[] bb, int ptr)
        {
            if (bb.Length == ptr)
            {
                return get_all_fields(obj.GetType(), DefaultBinding);
            }
            return enum_recursion(obj.GetType().GetField(bb[ptr], DefaultBinding).GetValue(obj), bb, ptr + 1);
        }

        public static List<FieldInfo> enum_recursion(object obj, string[] bb, int ptr, BindingFlags option)
        {
            if (bb.Length == ptr)
            {
                return obj.GetType().GetFields(option).ToList();
            }
            var x = obj.GetType().GetField(bb[ptr], DefaultBinding);
            return enum_recursion(obj.GetType().GetField(bb[ptr], DefaultBinding).GetValue(obj), bb, ptr + 1, option);
        }

        public static object get_recursion(object obj, string[] bb, int ptr)
        {
            if (bb.Length == ptr)
            {
                return obj;
            }
            return get_recursion(obj.GetType().GetField(bb[ptr], DefaultBinding).GetValue(obj), bb, ptr + 1);
        }

        public static void set_recursion(object obj, string[] bb, int ptr, object val)
        {
            if (bb.Length - 1 == ptr)
            {
                obj.GetType().GetField(bb[ptr]).SetValue(obj,
                    Convert.ChangeType(val, obj.GetType().GetField(bb[ptr], DefaultBinding).GetValue(obj).GetType()));
                return;
            }
            set_recursion(obj.GetType().GetField(bb[ptr]).GetValue(obj), bb, ptr + 1, val);
        }

        public static List<MethodInfo> enum_methods(object obj, string[] bb, int ptr, BindingFlags option)
        {
            if (bb.Length == ptr)
            {
                return obj.GetType().GetMethods(option).ToList();
            }
            var x = obj.GetType().GetField(bb[ptr], DefaultBinding);
            return enum_methods(obj.GetType().GetField(bb[ptr], DefaultBinding).GetValue(obj), bb, ptr + 1, option);
        }

        public static object call_method(object obj, string[] bb, int ptr, BindingFlags option, object[] param)
        {
            if (bb.Length - 1 == ptr)
            {
                return obj.GetType().GetMethods(option | BindingFlags.Static).Where(y => y.Name == bb[ptr]).ToList()[0].Invoke(obj, param);
            }
            var x = obj.GetType().GetField(bb[ptr], DefaultBinding | BindingFlags.Static);
            return call_method(obj.GetType().GetField(bb[ptr], DefaultBinding | BindingFlags.Static).GetValue(obj), bb, ptr + 1, option, param);
        }

        public static ParameterInfo[] get_method_paraminfo(object obj, string[] bb, int ptr, BindingFlags option)
        {
            if (bb.Length - 1 == ptr)
            {
                return obj.GetType().GetMethods(option).Where(y => y.Name == bb[ptr]).ToList()[0].GetParameters();
            }
            var x = obj.GetType().GetField(bb[ptr], DefaultBinding);
            return get_method_paraminfo(obj.GetType().GetField(bb[ptr], DefaultBinding).GetValue(obj), bb, ptr + 1, option);
        }

        #endregion

    }
}
