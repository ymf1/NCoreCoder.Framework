﻿using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using AspectCore.Extensions.Reflection.Emit;

namespace NCoreCoder.Aop
{
    public static class DynamicMethodExtension
    {
        public static void EmitProxyMethod(this ILGenerator ilGenerator, MethodInfo methodInfo,
            FieldBuilder _actors,FieldBuilder serviceProvider, FieldBuilder instance
        )
        {
            var returnType = methodInfo.ReturnType;
#if NETSTANDARD
            var isAsync = returnType == typeof(Task) || returnType.BaseType == typeof(Task) || returnType == typeof(ValueTask) || 
                          (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));
#else
            var isAsync = returnType == typeof(Task) || returnType.BaseType == typeof(Task) || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));
#endif
            var funcType = ExpressionExtension.CreateFuncType(isAsync ? returnType : typeof(object));

            var method = ilGenerator.DeclareLocal(typeof(MethodInfo));
            var parameters = ilGenerator.DeclareLocal(typeof(object[]));
            var _delegate = ilGenerator.DeclareLocal(typeof(Delegate));
            var _func = ilGenerator.DeclareLocal(funcType);

            var paramTypes = methodInfo
                .GetParameters()
                // ReSharper disable once InconsistentNaming
                .Select(_parameter => _parameter.ParameterType)
                .ToArray();

#region methodInfo
            ilGenerator.EmitMethod(methodInfo);
            ilGenerator.Emit(OpCodes.Stloc_0, method);
#endregion

#region args
            ilGenerator.AddLdcI4(paramTypes.Length);
            ilGenerator.Emit(OpCodes.Newarr, typeof(object));

            for (var j = 0; j < paramTypes.Length; j++)
            {
                ilGenerator.Emit(OpCodes.Dup);
                ilGenerator.AddLdcI4(j);
                ilGenerator.EmitLoadArg(j + 1);

                var parameterType = paramTypes[j];
                if (parameterType.GetTypeInfo().IsValueType || parameterType.IsGenericParameter)
                {
                    ilGenerator.Emit(OpCodes.Box, parameterType);
                }
                ilGenerator.Emit(OpCodes.Stelem_Ref);
            }

            ilGenerator.Emit(OpCodes.Stloc, parameters);
#endregion

#region Delegate
            ilGenerator.Emit(OpCodes.Ldtoken, isAsync ? returnType : typeof(object));
            ilGenerator.Emit(OpCodes.Call, MethodInfoExtension.GetTypeFromHandle);
            ilGenerator.Emit(OpCodes.Call, MethodInfoExtension.BuilderDelegate);
            ilGenerator.Emit(OpCodes.Stloc, _delegate);
#endregion

#region Convert To Func
            ilGenerator.Emit(OpCodes.Ldloc, _delegate);
            ilGenerator.Emit(OpCodes.Castclass, funcType);
            ilGenerator.Emit(OpCodes.Stloc, _func);
#endregion

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, _actors);
            ilGenerator.Emit(OpCodes.Ldloc, _func);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, serviceProvider);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, instance);
            ilGenerator.Emit(OpCodes.Ldloc, method);
            ilGenerator.Emit(OpCodes.Ldloc, parameters);

#region AopContext
            ilGenerator.Emit(OpCodes.Newobj, typeof(AopContext).GetConstructor(new[]
            {
                typeof(IServiceProvider),
                typeof(object),
                typeof(MethodInfo),
                typeof(object[])
            }));
#endregion

            if (isAsync)
            {
                if (returnType.IsGenericType)
                {
                    var typeInfo = returnType.GetTypeInfo();
                    var genericTypeDefinition = typeInfo.GetGenericArguments().Single();

                    if (returnType.BaseType == typeof(ValueType))
                        ilGenerator.Emit(OpCodes.Callvirt, MethodInfoExtension.ExecuteValueAsync.MakeGenericMethod(genericTypeDefinition));
                    else
                        ilGenerator.Emit(OpCodes.Callvirt, MethodInfoExtension.ExecuteAsync.MakeGenericMethod(genericTypeDefinition));
                }
                else
                {
#if NETSTANDARD
                    if (returnType == typeof(ValueTask))
                        ilGenerator.Emit(OpCodes.Callvirt, MethodInfoExtension.InvokeValueAsync);
                    else
                        ilGenerator.Emit(OpCodes.Callvirt, MethodInfoExtension.InvokeAsync);
#else
                    ilGenerator.Emit(OpCodes.Callvirt, MethodInfoExtension.InvokeAsync);
#endif
                }
            }
            else
            {
                //_actor.Execute(_invoke,context);
                ilGenerator.Emit(OpCodes.Callvirt, MethodInfoExtension.Execute);

                if (returnType == typeof(void))
                    ilGenerator.Emit(OpCodes.Pop);

                if (returnType != typeof(void) && returnType.IsValueType)
                    ilGenerator.Emit(OpCodes.Unbox_Any, returnType);

                if (!isAsync && returnType.IsGenericType)
                    ilGenerator.Emit(OpCodes.Castclass, returnType);
            }

            ilGenerator.Emit(OpCodes.Ret);
        }
    }
}