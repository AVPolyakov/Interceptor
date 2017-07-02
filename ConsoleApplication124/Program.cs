using System;
using System.Linq;
using System.Reflection;
using Autofac;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;

namespace ConsoleApplication124
{
    public interface IFooHandler : IHandler
    {
        FooResponse Do(FooRequest request);
    }

    public class FooHandler : IFooHandler
    {
        public FooResponse Do(FooRequest request)
        {
            return new FooResponse();
        }
    }

    public class FooRequest : IIdentifiable
    {
        public long Id { get; set; }
    }

    public class FooResponse
    {
    }

    public static class Program
    {
        static void Main()
        {
            var builder = new ContainerBuilder();
            builder.RegisterHandlers(typeof(Program).Assembly, new[] {
                typeof(IdentifiableInterceptor<,>),
            });

            var container = builder.Build();

            using (var scope = container.BeginLifetimeScope())
            {
                scope.Resolve<IFooHandler>().Do(new FooRequest {Id = 12});
            }
        }

        private static void RegisterHandlers(this ContainerBuilder builder, Assembly assembly, Type[] interceptorTypes)
        {
            foreach (var type in assembly.GetTypes())
            foreach (var @interface in type.GetInterfaces())
                if (@interface != typeof(IHandler) && typeof(IHandler).IsAssignableFrom(@interface))
                {
                    var registrationBuilder = builder.RegisterType(type).As(@interface);
                    if (interceptorTypes.Length > 0) registrationBuilder.EnableInterfaceInterceptors();
                    foreach (var method in @interface.GetMethods())
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1)
                        {
                            foreach (var interceptorType in interceptorTypes)
                            {
                                var genericArguments = interceptorType.GetGenericArguments();
                                if (genericArguments[0].GetGenericParameterConstraints()
                                        .All(_ => _.IsAssignableFrom(parameters[0].ParameterType)) &&
                                    genericArguments[1].GetGenericParameterConstraints()
                                        .All(_ => _.IsAssignableFrom(method.ReturnType)))
                                {
                                    builder.RegisterType(interceptorType.MakeGenericType(
                                            parameters[0].ParameterType, method.ReturnType))
                                        .As(typeof(IInterceptor<,>).MakeGenericType(
                                            parameters[0].ParameterType, method.ReturnType));
                                    var baseInterceptorType = typeof(Interceptor<,>).MakeGenericType(
                                        parameters[0].ParameterType, method.ReturnType);
                                    var interceptorId = Guid.NewGuid().ToString();
                                    builder.RegisterType(baseInterceptorType)
                                        .WithParameter("method", method).Named<IInterceptor>(interceptorId);
                                    registrationBuilder.InterceptedBy(interceptorId);
                                }
                            }
                        }
                    }
                }
        }
    }

    public interface IIdentifiable
    {
        long Id { get; set; }
    }

    public class IdentifiableInterceptor<TRequest, TResponse> : IInterceptor<TRequest, TResponse> 
        where TRequest : IIdentifiable
    {
        public TResponse Do(TRequest request, Func<TRequest, TResponse> func)
        {
            Console.WriteLine(request.Id);
            return func(request);
        }
    }

    public class Interceptor<TRequest, TResponse> : IInterceptor
    {
        private readonly MethodInfo method;
        private readonly IInterceptor<TRequest, TResponse> interceptor;

        public Interceptor(MethodInfo method, IInterceptor<TRequest, TResponse> interceptor)
        {
            this.method = method;
            this.interceptor = interceptor;
        }

        public void Intercept(IInvocation invocation)   
        {
            if (invocation.Method == method)
                invocation.ReturnValue = interceptor.Do((TRequest) invocation.Arguments.Single(), request => {
                    invocation.Arguments[0] = request;
                    invocation.Proceed();
                    return (TResponse) invocation.ReturnValue;
                });
            else
                invocation.Proceed();
        }
    }

    public interface IInterceptor<TRequest, TResponse>
    {
        TResponse Do(TRequest request, Func<TRequest, TResponse> func);
    }

    public interface IHandler
    {
    }
}
