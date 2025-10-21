// <copyright file="AutofacStartupModule.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>

using Autofac;
using Microsoft.Lumina.Client.ApiProxy;

namespace ApiProxyExample
{
    /// <summary>
    /// Autofac module for configuring Lumina API dependencies.
    /// This follows the pattern from the internal documentation.
    /// </summary>
    public class AutofacStartupModule : Module
    {
        /// <summary>
        /// Load the module dependencies.
        /// </summary>
        /// <param name="builder">The container builder.</param>
        protected override void Load(ContainerBuilder builder)
        {
            // Register LuminaServiceApiProxy as described in the documentation
            builder.RegisterType<LuminaServiceApiProxy>()
                .AsSelf()
                .SingleInstance();

            // Configure LuminaApiOptions from configuration
            builder.Register(c =>
            {
                var configuration = c.Resolve<IConfiguration>();
                var luminaApiOption = configuration
                    .GetSection("LuminaApiOptions")
                    .Get<LuminaApiOptions>() ?? new LuminaApiOptions();

                luminaApiOption.LuminaApiTokenProvider = async () =>
                {
                    // In a real implementation, this would get a proper access token
                    // For now, use the pattern from documentation
                    return await Task.FromResult("fake_token_for_testing");
                };

                return luminaApiOption;
            }).As<LuminaApiOptions>().SingleInstance();
        }
    }
}