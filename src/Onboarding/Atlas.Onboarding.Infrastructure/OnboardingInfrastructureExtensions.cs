using Atlas.Onboarding.Infrastructure.Persistence;
using Atlas.Onboarding.Infrastructure.Processing;
using Atlas.Onboarding.Infrastructure.Providers;
using Atlas.Onboarding.Infrastructure.Security;
using Atlas.Onboarding.Infrastructure.Storage;
using Atlas.Onboarding.Infrastructure.Submission;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Atlas.Onboarding.Infrastructure;

/// <summary>Registers everything the onboarding service needs from the outside world.</summary>
public static class OnboardingInfrastructureExtensions
{
    /// <summary>
    /// Registers persistence, document storage, provider clients, the submitter, the processor
    /// and the background worker.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// The configuration, with the sections Sql, DocumentStore, Processing, Providers and Secrets.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOnboardingInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SqlOptions>(configuration.GetSection("Sql"));
        services.Configure<DocumentStoreOptions>(configuration.GetSection("DocumentStore"));
        services.Configure<ProcessingOptions>(configuration.GetSection("Processing"));
        services.Configure<SecretsOptions>(configuration.GetSection("Secrets"));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MarketDatabases>();
        services.AddSingleton<IDocumentStore, AzureBlobDocumentStore>();
        services.AddSingleton<ApplicantHasher>();
        services.AddSingleton<StatusTokens>();
        services.AddSingleton<ProcessingSignal>();
        services.AddSingleton<ApplicationReader>();
        services.AddSingleton<ApplicationSubmitter>();
        services.AddSingleton<ApplicationProcessor>();
        services.AddSingleton<ProcessingWorker>();

        // A replica can be run without the worker, for example to deploy the API and the workers
        // separately. Any replica with a worker will pick up the work.
        if (configuration.GetValue("Processing:WorkerEnabled", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<ProcessingWorker>());
        }

        var providers = configuration.GetSection("Providers").Get<ProviderOptions>()
                        ?? new ProviderOptions();
        AddProvider<IIdentityVerifier, HttpIdentityVerifier>(services, providers);
        AddProvider<IScreeningService, HttpScreeningService>(services, providers);
        AddProvider<ICoreBanking, HttpCoreBanking>(services, providers);
        return services;
    }

    private static void AddProvider<TService, TImplementation>(IServiceCollection services,
        ProviderOptions providers)
        where TService : class
        where TImplementation : class, TService
    {
        // Retries, a timeout per attempt, a timeout overall, and a circuit breaker that stops
        // hammering a provider that is clearly down. The worker handles longer outages.
        services.AddHttpClient<TService, TImplementation>()
            .AddStandardResilienceHandler(options =>
            {
                var attempt = TimeSpan.FromSeconds(providers.AttemptTimeoutSeconds);
                options.AttemptTimeout.Timeout = attempt;
                options.TotalRequestTimeout.Timeout =
                    TimeSpan.FromSeconds(providers.TotalTimeoutSeconds);
                options.Retry.MaxRetryAttempts = providers.RetryAttempts;
                options.Retry.Delay = TimeSpan.FromMilliseconds(providers.RetryDelayMilliseconds);
                options.CircuitBreaker.SamplingDuration =
                    TimeSpan.FromSeconds(Math.Max(30, providers.AttemptTimeoutSeconds * 3));
            });
    }
}
