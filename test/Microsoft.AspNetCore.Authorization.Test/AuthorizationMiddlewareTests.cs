// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Authorization.Test
{
    public class AuthorizationMiddlewareTests
    {
        public class TestRequestDelegate
        {
            private readonly int _statusCode;

            public bool Called => CalledCount > 0;
            public int CalledCount { get; private set; }

            public Task RequestDelegate(HttpContext context)
            {
                CalledCount++;
                context.Response.StatusCode = _statusCode;
                return Task.CompletedTask;
            }

            public TestRequestDelegate(int statusCode = 200)
            {
                _statusCode = statusCode;
            }
        }

        private AuthorizationMiddleware CreateMiddleware(RequestDelegate requestDelegate = null, IAuthorizationPolicyProvider policyProvider = null)
        {
            requestDelegate = requestDelegate ?? ((context) => Task.CompletedTask);

            return new AuthorizationMiddleware(requestDelegate, policyProvider);
        }

        private Endpoint CreateEndpoint(params object[] metadata)
        {
            return new Endpoint(context => Task.CompletedTask, new EndpointMetadataCollection(metadata), "Test endpoint");
        }

        [Fact]
        public async Task NoEndpoint_AnonymousUser_Allows()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(anonymous: true);

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.True(next.Called);
        }

        [Fact]
        public async Task HasEndpointWithoutAuth_AnonymousUser_Allows()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(anonymous: true, endpoint: CreateEndpoint());

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.True(next.Called);
        }

        private class TestAuthenticationService : IAuthenticationService
        {
            public bool ChallengeCalled { get; private set; }
            public bool ForbidCalled { get; private set; }
            public bool AuthenticateCalled { get; private set; }

            public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string scheme)
            {
                AuthenticateCalled = true;
                return Task.FromResult(AuthenticateResult.Fail("Denied"));
            }

            public Task ChallengeAsync(HttpContext context, string scheme, AuthenticationProperties properties)
            {
                ChallengeCalled = true;
                return Task.CompletedTask;
            }

            public Task ForbidAsync(HttpContext context, string scheme, AuthenticationProperties properties)
            {
                ForbidCalled = true;
                return Task.CompletedTask;
            }

            public Task SignInAsync(HttpContext context, string scheme, ClaimsPrincipal principal, AuthenticationProperties properties)
            {
                throw new NotImplementedException();
            }

            public Task SignOutAsync(HttpContext context, string scheme, AuthenticationProperties properties)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public async Task HasEndpointWithAuth_AnonymousUser_Challenges()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();
            var authenticationService = new TestAuthenticationService();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(anonymous: true, endpoint: CreateEndpoint(new AuthorizeAttribute()), authenticationService: authenticationService);

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.False(next.Called);
            Assert.True(authenticationService.ChallengeCalled);
        }

        [Fact]
        public async Task OnAuthorizationAsync_WillCallPolicyProvider()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            var getPolicyCount = 0;
            policyProvider.Setup(p => p.GetPolicyAsync(It.IsAny<string>())).ReturnsAsync(policy)
                .Callback(() => getPolicyCount++);
            var next = new TestRequestDelegate();
            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(anonymous: true, endpoint: CreateEndpoint(new AuthorizeAttribute("whatever")));

            // Act & Assert
            await middleware.Invoke(context);
            Assert.Equal(1, getPolicyCount);
            Assert.Equal(1, next.CalledCount);

            await middleware.Invoke(context);
            Assert.Equal(2, getPolicyCount);
            Assert.Equal(2, next.CalledCount);

            await middleware.Invoke(context);
            Assert.Equal(3, getPolicyCount);
            Assert.Equal(3, next.CalledCount);
        }

        [Fact]
        public async Task Invoke_ValidClaimShouldNotFail()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireClaim("Permission", "CanViewPage").Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(endpoint: CreateEndpoint(new AuthorizeAttribute()));

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.True(next.Called);
        }

        [Fact]
        public async Task HasEndpointWithAuthAndAllowAnonymous_AnonymousUser_Allows()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();
            var authenticationService = new TestAuthenticationService();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(anonymous: true, endpoint: CreateEndpoint(new AuthorizeAttribute(), new AllowAnonymousAttribute()), authenticationService: authenticationService);

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.True(next.Called);
            Assert.False(authenticationService.ChallengeCalled);
        }

        [Fact]
        public async Task HasEndpointWithAuth_AuthenticatedUser_Allows()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();
            var authenticationService = new TestAuthenticationService();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(endpoint: CreateEndpoint(new AuthorizeAttribute()), authenticationService: authenticationService);

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.True(next.Called);
            Assert.False(authenticationService.ChallengeCalled);
        }

        [Fact]
        public async Task Invoke_AuthSchemesFailShouldSetEmptyPrincipalOnContext()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder("Fails").RequireAuthenticatedUser().Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();
            var authenticationService = new TestAuthenticationService();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(endpoint: CreateEndpoint(new AuthorizeAttribute()), authenticationService: authenticationService);

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.False(next.Called);
            Assert.NotNull(context.User?.Identity);
            Assert.True(authenticationService.AuthenticateCalled);
            Assert.True(authenticationService.ChallengeCalled);
        }

        [Fact]
        public async Task Invoke_SingleValidClaimShouldSucceed()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireClaim("Permission", "CanViewComment", "CanViewPage").Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(endpoint: CreateEndpoint(new AuthorizeAttribute()));

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.True(next.Called);
        }

        [Fact]
        public async Task AuthZResourceShouldBeEndpoint()
        {
            // Arrange
            object resource = null;
            var policy = new AuthorizationPolicyBuilder().RequireAssertion(c =>
            {
                resource = c.Resource;
                return true;
            }).Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var endpoint = CreateEndpoint(new AuthorizeAttribute());
            var context = GetHttpContext(endpoint: endpoint);

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.Equal(endpoint, resource);
        }

        [Fact]
        public async Task Invoke_RequireUnknownRoleShouldForbid()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder().RequireRole("Wut").Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();
            var authenticationService = new TestAuthenticationService();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(endpoint: CreateEndpoint(new AuthorizeAttribute()), authenticationService: authenticationService);

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.False(next.Called);
            Assert.False(authenticationService.ChallengeCalled);
            Assert.True(authenticationService.ForbidCalled);
        }

        [Fact]
        public async Task Invoke_InvalidClaimShouldForbid()
        {
            // Arrange
            var policy = new AuthorizationPolicyBuilder()
                .RequireClaim("Permission", "CanViewComment")
                .Build();
            var policyProvider = new Mock<IAuthorizationPolicyProvider>();
            policyProvider.Setup(p => p.GetDefaultPolicyAsync()).ReturnsAsync(policy);
            var next = new TestRequestDelegate();
            var authenticationService = new TestAuthenticationService();

            var middleware = CreateMiddleware(next.RequestDelegate, policyProvider.Object);
            var context = GetHttpContext(endpoint: CreateEndpoint(new AuthorizeAttribute()), authenticationService: authenticationService);

            // Act
            await middleware.Invoke(context);

            // Assert
            Assert.False(next.Called);
            Assert.False(authenticationService.ChallengeCalled);
            Assert.True(authenticationService.ForbidCalled);
        }

        private HttpContext GetHttpContext(
            bool anonymous = false,
            Action<IServiceCollection> registerServices = null,
            Endpoint endpoint = null,
            IAuthenticationService authenticationService = null)
        {
            var basicPrincipal = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new Claim[] {
                        new Claim("Permission", "CanViewPage"),
                        new Claim(ClaimTypes.Role, "Administrator"),
                        new Claim(ClaimTypes.Role, "User"),
                        new Claim(ClaimTypes.NameIdentifier, "John")},
                        "Basic"));

            var validUser = basicPrincipal;

            var bearerIdentity = new ClaimsIdentity(
                    new Claim[] {
                        new Claim("Permission", "CupBearer"),
                        new Claim(ClaimTypes.Role, "Token"),
                        new Claim(ClaimTypes.NameIdentifier, "John Bear")},
                        "Bearer");

            var bearerPrincipal = new ClaimsPrincipal(bearerIdentity);

            validUser.AddIdentity(bearerIdentity);

            // ServiceProvider
            var serviceCollection = new ServiceCollection();

            authenticationService = authenticationService ?? Mock.Of<IAuthenticationService>();

            serviceCollection.AddSingleton(authenticationService);
            serviceCollection.AddOptions();
            serviceCollection.AddLogging();
            serviceCollection.AddAuthorization();
            serviceCollection.AddAuthorizationPolicyEvaluator();
            if (registerServices != null)
            {
                registerServices(serviceCollection);
            }

            var serviceProvider = serviceCollection.BuildServiceProvider();

            //// HttpContext
            var httpContext = new DefaultHttpContext();
            if (endpoint != null)
            {
                var endpointFeature = Mock.Of<IEndpointFeature>();
                endpointFeature.Endpoint = endpoint;

                httpContext.Features.Set<IEndpointFeature>(endpointFeature);
            }
            httpContext.RequestServices = serviceProvider;
            //auth.Setup(c => c.AuthenticateAsync(httpContext.Object, "Bearer")).ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(bearerPrincipal, "Bearer")));
            //auth.Setup(c => c.AuthenticateAsync(httpContext.Object, "Basic")).ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(basicPrincipal, "Basic")));
            //auth.Setup(c => c.AuthenticateAsync(httpContext.Object, "Fails")).ReturnsAsync(AuthenticateResult.Fail("Fails"));
            //httpContext.SetupProperty(c => c.User);
            if (!anonymous)
            {
                httpContext.User = validUser;
            }
            //httpContext.SetupGet(c => c.RequestServices).Returns(serviceProvider);

            //// AuthorizationFilterContext
            //var actionContext = new ActionContext(
            //    httpContext: httpContext.Object,
            //    routeData: new RouteData(),
            //    actionDescriptor: new ActionDescriptor());

            //var authorizationContext = new Filters.AuthorizationFilterContext(
            //    actionContext,
            //    Enumerable.Empty<IFilterMetadata>().ToList()
            //);

            return httpContext;
        }
    }
}