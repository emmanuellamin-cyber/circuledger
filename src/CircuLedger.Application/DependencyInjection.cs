using CircuLedger.Application.Certificates;
using CircuLedger.Application.Common;
using CircuLedger.Application.Dashboard;
using CircuLedger.Application.Devices;
using CircuLedger.Application.Dispatch;
using CircuLedger.Application.DropOffPoints;
using CircuLedger.Application.Funds;
using CircuLedger.Application.Intake;
using CircuLedger.Application.Marketplace;
using CircuLedger.Application.People;
using CircuLedger.Application.Purchasing;
using CircuLedger.Application.Recovery;
using CircuLedger.Application.Sanitization;
using CircuLedger.Application.Sms;
using CircuLedger.Application.Ussd;
using FluentValidation;
using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CircuLedger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HubOptions>(configuration.GetSection(HubOptions.Section));
        services.Configure<DispatchOptions>(configuration.GetSection(DispatchOptions.Section));
        services.Configure<PilotTargetOptions>(configuration.GetSection(PilotTargetOptions.Section));
        services.Configure<UssdOptions>(configuration.GetSection(UssdOptions.Section));
        services.Configure<UploadOptions>(configuration.GetSection(UploadOptions.Section));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        TypeAdapterConfig.GlobalSettings.Default.IgnoreNullValues(false).PreserveReference(false);
        TypeAdapterConfig.GlobalSettings.Scan(typeof(DependencyInjection).Assembly);

        services.AddScoped<CitizenNotifier>();
        services.AddScoped<ICitizenService, CitizenService>();
        services.AddScoped<IDropOffPointService, DropOffPointService>();
        services.AddScoped<IIntakeService, IntakeService>();
        services.AddScoped<IUssdService, UssdService>();
        services.AddScoped<ISmsInboundService, SmsInboundService>();
        services.AddScoped<IDispatchService, DispatchService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<ISanitizationService, SanitizationService>();
        services.AddScoped<ICertificateService, CertificateService>();
        services.AddScoped<ISafetyTrainingGuard, SafetyTrainingGuard>();
        services.AddScoped<IRecoveryService, RecoveryService>();
        services.AddScoped<IListingService, ListingService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IReinvestmentService, ReinvestmentService>();
        services.AddScoped<IFundService, FundService>();
        services.AddScoped<ITrainingService, TrainingService>();
        services.AddScoped<IEmploymentService, EmploymentService>();
        services.AddScoped<IAwarenessService, AwarenessService>();
        services.AddScoped<IDashboardService, DashboardService>();
        return services;
    }
}
