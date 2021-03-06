﻿// ----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ----------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using Microsoft.Azure.Mobile.Server.Cache;
using Microsoft.Azure.Mobile.Server.Serialization;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using TestUtilities;
using Xunit;

namespace Microsoft.Azure.Mobile.Server.Config
{
    public class MobileAppControllerAttributeTests
    {
        private HttpConfiguration nullConfig = new HttpConfiguration();
        private HttpConfiguration config;
        private Mock<ICachePolicyProvider> providerMock;
        private HttpRequestMessage request;
        private MobileAppControllerAttribute mobileAppControllerAttr;
        private HttpActionExecutedContext actionExecutedContext;
        private HttpControllerContext controllerContext;
        private HttpActionContext actionContext;

        public MobileAppControllerAttributeTests()
        {
            this.providerMock = new Mock<ICachePolicyProvider>();
            this.config = new HttpConfiguration();
            this.config.SetCachePolicyProvider(this.providerMock.Object);

            this.request = new HttpRequestMessage();
            this.mobileAppControllerAttr = new MobileAppControllerAttribute();
            this.actionExecutedContext = new HttpActionExecutedContext();
            this.controllerContext = new HttpControllerContext();
            this.controllerContext.Configuration = this.nullConfig;
            this.controllerContext.Request = this.request;
            this.actionContext = new HttpActionContext();
            this.actionContext.ControllerContext = this.controllerContext;
            this.actionExecutedContext.ActionContext = this.actionContext;
        }

        public static TheoryDataCollection<HttpMethod, bool> CacheableHttpMethods
        {
            get
            {
                return new TheoryDataCollection<HttpMethod, bool>
                {
                    { null, false },
                    { HttpMethod.Get, true },
                    { HttpMethod.Head, true },
                    { HttpMethod.Delete, false },
                    { HttpMethod.Options, false },
                    { HttpMethod.Post, false },
                    { HttpMethod.Put, false },
                    { HttpMethod.Trace, false },
                    { new HttpMethod("PATCH"), false },
                    { new HttpMethod("UNKNOWN"), false },
                };
            }
        }

        public static TheoryDataCollection<HttpResponseMessage, bool> CacheResponses
        {
            get
            {
                HttpResponseMessage rspNone = new HttpResponseMessage();
                rspNone.Headers.ETag = new EntityTagHeaderValue("\"quotedstring\"");

                HttpResponseMessage rspCacheControl1 = new HttpResponseMessage();
                rspCacheControl1.Headers.CacheControl = new CacheControlHeaderValue();

                HttpResponseMessage rspCacheControl2 = new HttpResponseMessage();
                rspCacheControl2.Headers.CacheControl = new CacheControlHeaderValue();
                rspCacheControl2.Headers.CacheControl.NoCache = true;

                HttpResponseMessage rspExpires1 = new HttpResponseMessage();
                rspExpires1.Content = new StringContent("Hello");
                rspExpires1.Content.Headers.Expires = DateTimeOffset.UtcNow;

                HttpResponseMessage rspExpires2 = new HttpResponseMessage();
                rspExpires2.Content = new StringContent("Hello");
                rspExpires2.Content.Headers.TryAddWithoutValidation("Expires", "0");

                return new TheoryDataCollection<HttpResponseMessage, bool>
                {
                    { null, false },
                    { rspNone, false },
                    { new HttpResponseMessage(), false },
                    { rspCacheControl1, true },
                    { rspCacheControl2, true },
                    { rspExpires1, true },
                    { rspExpires2, true },
                };
            }
        }

        public static TheoryDataCollection<HttpMethod, HttpResponseMessage, bool> CallSetPolicy
        {
            get
            {
                HttpResponseMessage rspNone = new HttpResponseMessage();
                rspNone.Headers.ETag = new EntityTagHeaderValue("\"quotedstring\"");

                HttpResponseMessage rspCacheControl1 = new HttpResponseMessage();
                rspCacheControl1.Headers.CacheControl = new CacheControlHeaderValue();

                HttpResponseMessage rspCacheControl2 = new HttpResponseMessage();
                rspCacheControl2.Headers.CacheControl = new CacheControlHeaderValue();
                rspCacheControl2.Headers.CacheControl.NoCache = true;

                HttpResponseMessage rspExpires1 = new HttpResponseMessage();
                rspExpires1.Content = new StringContent("Hello");
                rspExpires1.Content.Headers.Expires = DateTimeOffset.UtcNow;

                HttpResponseMessage rspExpires2 = new HttpResponseMessage();
                rspExpires2.Content = new StringContent("Hello");
                rspExpires2.Content.Headers.TryAddWithoutValidation("Expires", "0");

                return new TheoryDataCollection<HttpMethod, HttpResponseMessage, bool>
                {
                    { HttpMethod.Get, rspNone, true },
                    { HttpMethod.Get, null, false },
                    { HttpMethod.Post, rspNone, false },
                    { HttpMethod.Get, rspCacheControl1, false },
                    { HttpMethod.Post, rspCacheControl1, false },
                    { HttpMethod.Get, rspCacheControl2, false },
                    { HttpMethod.Post, rspCacheControl2, false },
                    { HttpMethod.Get, rspExpires1, false },
                    { HttpMethod.Post, rspExpires1, false },
                };
            }
        }

        [Fact]
        public void Initialize_Initializes_SerializerSettings()
        {
            // Arrange
            var attr = new MobileAppControllerAttribute();
            var settings = new HttpControllerSettings(this.nullConfig);

            // Act
            attr.Initialize(settings, null);

            // Assert
            // Verify SerializerSettings are set up as we expect
            var serializerSettings = settings.Formatters.JsonFormatter.SerializerSettings;
            Assert.Equal(typeof(ServiceContractResolver), serializerSettings.ContractResolver.GetType());
            Assert.Equal(DefaultValueHandling.Include, serializerSettings.DefaultValueHandling);
            Assert.Equal(NullValueHandling.Include, serializerSettings.NullValueHandling);

            // Verify Converters
            var stringEnumConverter = serializerSettings.Converters.Single(c => c.GetType() == typeof(StringEnumConverter)) as StringEnumConverter;
            Assert.True(stringEnumConverter.CamelCaseText);

            var isoDateTimeConverter = serializerSettings.Converters.Single(c => c.GetType() == typeof(IsoDateTimeConverter)) as IsoDateTimeConverter;
            Assert.Equal(DateTimeStyles.AdjustToUniversal, isoDateTimeConverter.DateTimeStyles);
            Assert.Equal("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFZ", isoDateTimeConverter.DateTimeFormat);
            Assert.Equal(CultureInfo.InvariantCulture, isoDateTimeConverter.Culture);

            Assert.NotSame(this.nullConfig.Formatters.JsonFormatter.SerializerSettings.ContractResolver, settings.Formatters.JsonFormatter.SerializerSettings.ContractResolver);
            Assert.Same(settings.Formatters.JsonFormatter, settings.Formatters[0]);
        }

        [Theory]
        [MemberData("CacheResponses")]
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "notUsed", Justification = "Part of test data")]
        public void SendAsync_PassesResponseThrough_IfNullCachePolicyProvider(HttpResponseMessage response, bool notUsed)
        {
            // Arrange
            this.actionExecutedContext.Response = response;

            // Act
            this.mobileAppControllerAttr.OnActionExecuted(this.actionExecutedContext);

            // Assert
            // Make sure the response didn't change
            Assert.Equal(response, this.actionExecutedContext.Response);
        }

        [Theory]
        [MemberData("CallSetPolicy")]
        public void SendAsync_CallsSetPolicy_IfCacheableMethodAndNoCacheHeadersPresent(HttpMethod method, HttpResponseMessage response, bool shouldCall)
        {
            // Arrange
            this.controllerContext.Request = new HttpRequestMessage(method, "http://localhost");
            this.controllerContext.Configuration = this.config;
            this.actionExecutedContext.Response = response;

            // Act
            this.mobileAppControllerAttr.OnActionExecuted(this.actionExecutedContext);

            // Assert
            this.providerMock.Verify(p => p.SetCachePolicy(response), shouldCall ? Times.Once() : Times.Never());
        }

        [Theory]
        [MemberData("CacheableHttpMethods")]
        public void IsCacheableMethod_ReturnsTrueForCacheableMethods(HttpMethod method, bool expected)
        {
            // Act
            bool actual = MobileAppControllerAttribute.IsCacheableMethod(method);

            // Assert
            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData("CacheResponses")]
        public void HasCachingHeaders_ReturnsTrueWhenCacheControlOrExpiresIsPresent(HttpResponseMessage response, bool expected)
        {
            // Act
            bool actual = MobileAppControllerAttribute.HasCachingHeaders(response);

            // Assert
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SetVersionHeader_SetsVersionHeader()
        {
            // Arrange
            HttpResponseMessage response = new HttpResponseMessage();
            Assembly asm = typeof(MobileAppControllerAttribute).Assembly;
            string expected = "net-" + AssemblyUtils.GetExecutingAssemblyFileVersionOrDefault(asm);

            // Act
            MobileAppControllerAttribute.SetVersionHeader(response);
            string actual = response.Headers.GetValues("x-zumo-server-version").Single();

            // Assert
            Assert.Equal(expected, actual);
        }
    }
}