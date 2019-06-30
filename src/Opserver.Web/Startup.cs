﻿using System.Collections;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Opserver.Data;
using StackExchange.Opserver.Helpers;
using StackExchange.Profiling;

namespace StackExchange.Opserver
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void TODO()
        {
            Cache.EnableProfiling = SiteSettings.PollerProfiling;
            Cache.LogExceptions = SiteSettings.LogPollerExceptions;
            // When settings change, reload the app pool
            //Current.Settings.OnChanged += HttpRuntime.UnloadAppDomain;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<OpserverSettings>(_configuration);
            services.AddTransient(s => s.GetRequiredService<IOptions<OpserverSettings>>().Value);
            services.AddStatusModules(_configuration);
            services.AddResponseCaching();
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(options =>
                    {
                        options.LoginPath = "/login";
                        options.LoginPath = "/logout";
                    });
            services.AddHttpContextAccessor()
                    .AddMemoryCache()
                    .AddExceptional(
                _configuration.GetSection("Exceptional"),
                settings =>
                {
                    settings.UseExceptionalPageOnThrow = true;
                    settings.GetCustomData = (ex, data) =>
                    {
                        // everything below needs a context
                        if (Current.Context != null && Current.User != null)
                        {
                            data.Add("User", Current.User.AccountName);
                            data.Add("Roles", Current.User.Roles.ToString());
                        }

                        while (ex != null)
                        {
                            foreach (DictionaryEntry de in ex.Data)
                            {
                                var key = de.Key as string;
                                if (key.HasValue() && key.StartsWith(ExtensionMethods.ExceptionLogPrefix))
                                {
                                    data.Add(key.Replace(ExtensionMethods.ExceptionLogPrefix, ""), de.Value?.ToString() ?? "");
                                }
                            }
                            ex = ex.InnerException;
                        }
                    };
                });

            services.AddSingleton<IHostedService, PollingService>();
            services.AddSingleton<IConfigureOptions<MiniProfilerOptions>, MiniProfilerCacheStorageDefaults>();
            //services.AddMiniProfiler(options =>
            //{
            //    options.RouteBasePath = "/profiler/";
            //    options.PopupRenderPosition = RenderPosition.Left;
            //    options.PopupMaxTracesToShow = 5;
            //    options.ShouldProfile = req =>
            //    {
            //        var conn = req.HttpContext.Connection;
            //        switch (SiteSettings.ProfilingMode)
            //        {
            //            case SiteSettings.ProfilingModes.Enabled:
            //                return true;
            //            case SiteSettings.ProfilingModes.LocalOnly:
            //                return conn.RemoteIpAddress.Equals(conn.LocalIpAddress) || IPAddress.IsLoopback(conn.RemoteIpAddress);
            //            case SiteSettings.ProfilingModes.AdminOnly:
            //                return Current.User?.IsGlobalAdmin == true;
            //            default:
            //                return false;
            //        }
            //    };
            //    options.IgnorePath("/graph")
            //           .IgnorePath("/login")
            //           .IgnorePath("/spark")
            //           .IgnorePath("/top-refresh");
            //});
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);
            services.AddMvc();
        }

        public void Configure(
            IApplicationBuilder appBuilder,
            IHostApplicationLifetime appLifetime
        )
        {
            appBuilder.UseStaticFiles()
                      .UseExceptional()
                      //.UseMiniProfiler()
                      .UseAuthentication()
                      .UseAuthorization()
                      .UseRouting()
                      .Use(async (httpContext, next)  =>
                      {
                          Current.SetContext(new Current.CurrentContext(httpContext));
                          await next();
                      })
                      .UseEndpoints(endpoints =>
                      {
                          endpoints.MapDefaultControllerRoute();
                      });
            appLifetime.ApplicationStopping.Register(OnShutdown);
        }

        private void OnShutdown()
        {
            PollingEngine.StopPolling();
        }
    }
}