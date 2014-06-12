﻿using System;
using System.Reflection.Emit;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows;
using System.Reflection;
using EventBinding.MVVM;

namespace EventBinding
{
    public class EventBindingExtension : MarkupExtension
    {
        public string Command { get; set; }
        public string CommandParameter { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var targetProvider = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
            if (targetProvider == null)
            {
                throw new InvalidOperationException();
            }

            var targetObject = targetProvider.TargetObject as FrameworkElement;
            if (targetObject == null)
            {
                throw new InvalidOperationException();
            }

            var memberInfo = targetProvider.TargetProperty as MemberInfo;
            if (memberInfo == null)
            {
                throw new InvalidOperationException();
            }

            if (string.IsNullOrWhiteSpace(Command))
            {
                Command = memberInfo.Name.Replace("Add", "");
                if (Command.Contains("Handler"))
                {
                    Command = Command.Replace("Handler", "Command");
                }
                else
                {
                    Command = Command + "Command";
                }
            }

            return CreateHandler(memberInfo, Command, targetObject.GetType());
        }

        private Type GetEventHandlerType(MemberInfo memberInfo)
        {
            Type eventHandlerType = null;
            if (memberInfo is EventInfo)
            {
                var info = memberInfo as EventInfo;
                var eventInfo = info;
                eventHandlerType = eventInfo.EventHandlerType;
            }
            else if (memberInfo is MethodInfo)
            {
                var info = memberInfo as MethodInfo;
                var methodInfo = info;
                ParameterInfo[] pars = methodInfo.GetParameters();
                eventHandlerType = pars[1].ParameterType;
            }

            return eventHandlerType;
        }

        private object CreateHandler(MemberInfo memberInfo, string cmdName, Type targetType)
        {
            Type eventHandlerType = GetEventHandlerType(memberInfo);

            if (eventHandlerType == null) return null;

            var handlerInfo = eventHandlerType.GetMethod("Invoke");
            var method = new DynamicMethod("", handlerInfo.ReturnType,
                new Type[]
                {
                    handlerInfo.GetParameters()[0].ParameterType,
                    handlerInfo.GetParameters()[1].ParameterType,
                });

            var gen = method.GetILGenerator();
            gen.Emit(OpCodes.Ldarg, 0);
            gen.Emit(OpCodes.Ldarg, 1);
            gen.Emit(OpCodes.Ldstr, cmdName);
            gen.Emit(OpCodes.Ldstr, CommandParameter ?? string.Empty);
            gen.Emit(OpCodes.Call, getMethod);
            gen.Emit(OpCodes.Ret);

            return method.CreateDelegate(eventHandlerType);
        }

        static readonly MethodInfo getMethod = typeof(EventBindingExtension).GetMethod("HandlerIntern", new Type[] { typeof(object), typeof(object), typeof(string), typeof(string) });

        public static void HandlerIntern(object sender, object args, string cmdName, string commandParameter)
        {
            var fe = sender as FrameworkElement;
            if (fe != null)
            {
                ICommand cmd = GetCommand(fe, cmdName);
                object commandParam = null;
                if (!string.IsNullOrWhiteSpace(commandParameter))
                {
                    commandParam = GetCommandParameter(fe, args, commandParameter);
                }
                if ((cmd != null) && cmd.CanExecute(commandParam))
                {
                    cmd.Execute(commandParam);
                }
            }
        }

        internal static ICommand GetCommand(FrameworkElement target, string cmdName)
        {
            var vm = FindViewModel(target);
            if (vm == null) return null;

            var vmType = vm.GetType();
            var cmdProp = vmType.GetProperty(cmdName);
            if (cmdProp != null)
            {
                return cmdProp.GetValue(vm) as ICommand;
            }
#if DEBUG
            throw new Exception("EventBinding path error: '" + cmdName + "' property not found on '" + vmType + "' 'DelegateCommand'");
#endif

            return null;
        }

        internal static object GetCommandParameter(FrameworkElement target, object args, string commandParameter)
        {
            object ret = null;
            var classify = commandParameter.Split('.');
            switch (classify[0])
            {
                case "$e":
                    ret = args;
                    break;
                case "$this":
                    if (classify.Length > 1)
                    {
                        ret = FollowPropertyPath(target, commandParameter.Replace("$this.", ""), target.GetType());
                    }
                    else { ret = target; }
                    break;
            }

            return ret;
        }

        internal static ViewModelBase FindViewModel(FrameworkElement target)
        {
            if (target == null) return null;

            var vm = target.DataContext as ViewModelBase;
            if (vm != null) return vm;

            var parent = target.GetParentObject() as FrameworkElement;

            return FindViewModel(parent);
        }

        internal static object FollowPropertyPath(object value, string path, Type valueType = null)
        {
            Type currentType = valueType ?? value.GetType();

            foreach (string propertyName in path.Split('.'))
            {
                PropertyInfo property = currentType.GetProperty(propertyName);
                value = property.GetValue(value);
                currentType = property.PropertyType;
            }
            return value;
        }
    }
}
