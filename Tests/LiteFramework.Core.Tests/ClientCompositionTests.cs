using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class ClientCompositionTests
    {
        private interface IProduct { }
        private sealed class Product : IProduct, IDisposable
        {
            public int DisposeCount;
            public void Dispose() => DisposeCount++;
        }

        private sealed class MissingConsumer { public MissingConsumer(IProduct product) { } }
        private sealed class ThrowingDisposer : IDisposable
        {
            public void Dispose() => throw new InvalidOperationException("service dispose failed");
        }

        private sealed class Module : IClientModule
        {
            private readonly Action<ClientContext> _initialize;
            private readonly Action _shutdown;
            public string Name => "Composition";
            public Module(Action<ClientContext> initialize, Action shutdown = null)
            {
                _initialize = initialize;
                _shutdown = shutdown;
            }
            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _initialize(context);
                return UniTask.CompletedTask;
            }
            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _shutdown?.Invoke();
                return UniTask.CompletedTask;
            }
        }

        [Fact]
        public void ContextAndRootContainerExposeSameProductsAndAliases()
        {
            using var root = new ClientScope("Root");
            var context = new ClientContext(root);
            var product = new Product();
            context.Put(product);
            context.Put<IProduct>(product);
            context.Put(product);
            Assert.Same(product, context.Require<Product>());
            Assert.Same(product, context.Services.Resolve<IProduct>());
            Assert.Same(context.Services, context.Require<ServiceContainer>());
            Assert.Same(context.Services, context.Require<IServiceContainer>());
            Assert.Same(root, context.Require<ClientScope>());
        }

        [Fact]
        public void ProductConflictNullAndWritesAfterSealFailExplicitly()
        {
            using var root = new ClientScope("Root");
            var context = new ClientContext(root);
            var product = new Product();
            context.Put(product);
            Assert.Throws<InvalidOperationException>(() => context.Put(new Product()));
            Assert.Throws<ArgumentNullException>(() => context.Put<Product>(null));
            context.Services.Seal();
            Assert.Throws<InvalidOperationException>(() => context.Put(product));
            Assert.Same(product, context.Require<Product>());
        }

        [Fact]
        public void MissingProductCanBeOptionalButBrokenRegisteredGraphCannot()
        {
            using var root = new ClientScope("Root");
            var context = new ClientContext(root);
            Assert.Null(context.Get<IProduct>());
            Assert.Throws<InvalidOperationException>(() => context.Require<IProduct>());
            context.Services.Register<MissingConsumer, MissingConsumer>();
            Assert.Throws<InvalidOperationException>(() => context.Get<MissingConsumer>());
        }

        [Fact]
        public void DirectContainerRegistrationIsVisibleToLaterModules()
        {
            using var root = new ClientScope("Root");
            var context = new ClientContext(root);
            var product = new Product();
            context.Services.RegisterInstance<IProduct>(product);
            Assert.Same(product, context.Require<IProduct>());
            root.Dispose();
            Assert.Equal(0, product.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => context.Get<IProduct>());
        }

        [Fact]
        public async Task HostSealsSingleSourceAndPreservesModuleOwnership()
        {
            var product = new Product();
            var host = new ClientHost();
            ClientContext assembly = null;
            host.AddModule(new Module(context => { assembly = context; context.Put(product); }, product.Dispose));
            host.AddModule(new Module(context => context.Put<IProduct>(context.Require<Product>())));
            await host.InitializeAsync();
            Assert.Same(product, host.Product<IProduct>());
            Assert.Same(assembly.Services, host.Product<ServiceContainer>());
            Assert.True(assembly.Services.IsSealed);
            await host.ShutdownAsync();
            Assert.Equal(1, product.DisposeCount);
            Assert.True(assembly.Services.IsDisposed);
        }

        [Fact]
        public async Task HostPreflightFailureRollsBackCompletedModulesAndOwnedServices()
        {
            var trace = new List<string>();
            var product = new Product();
            var host = new ClientHost();
            host.AddModule(new Module(context =>
            {
                context.Services.RegisterInstance(product, ServiceOwnership.ContainerOwned);
                context.Services.Register<MissingConsumer, MissingConsumer>();
            }, () => trace.Add("module shutdown")));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.InitializeAsync());
            Assert.Equal(new[] { "module shutdown" }, trace);
            Assert.Equal(1, product.DisposeCount);
            Assert.True(host.RootScope.IsDisposed);
            Assert.Equal(4, host.State);
        }

        [Fact]
        public async Task ProductsCreatedByFailingModuleStillHaveOwnedCleanup()
        {
            var product = new Product();
            var host = new ClientHost();
            host.AddModule(new Module(context =>
            {
                context.Services.RegisterInstance(product, ServiceOwnership.ContainerOwned);
                throw new InvalidOperationException("init failed");
            }));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.InitializeAsync());
            Assert.Equal(1, product.DisposeCount);
        }

        [Fact]
        public async Task ServiceReleaseErrorsReachHostDiagnosticsOnce()
        {
            var product = new Product();
            var host = new ClientHost();
            host.AddModule(new Module(context =>
            {
                context.Services.RegisterInstance(product, ServiceOwnership.ContainerOwned);
                context.Services.RegisterInstance(new ThrowingDisposer(), ServiceOwnership.ContainerOwned);
            }));
            await host.InitializeAsync();
            await host.ShutdownAsync();
            await host.ShutdownAsync();
            Assert.Equal(1, product.DisposeCount);
            Assert.Single(host.ShutdownFailures);
            Assert.Contains("service dispose failed", host.ShutdownFailures[0].Message);
        }

        [Fact]
        public async Task ContainerModuleCanReleaseServicesBeforeBorrowedFacilitiesShutdown()
        {
            var trace = new List<string>();
            var host = new ClientHost();
            ServiceContainer container = null;
            host.AddModule(new Module(context => container = context.Services, () => trace.Add("facility shutdown")));
            host.AddModule(new Module(context =>
                context.Services.RegisterFactory<Product>(_ => new Product()), () =>
            {
                container.Dispose();
                trace.Add("services shutdown");
            }));
            await host.InitializeAsync();
            var product = host.Product<Product>();
            await host.ShutdownAsync();
            Assert.Equal(new[] { "services shutdown", "facility shutdown" }, trace);
            Assert.Equal(1, product.DisposeCount);
        }
    }
}
