using System.Text.Json.Serialization;

namespace RepoInsight.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ArchitectureNodeType>))]
public enum ArchitectureNodeType
{
    Controller, Service, Module, Guard, Strategy, Dto, DataAccessService, WebhookController,
    Repository, DbContext, Middleware, HostedService, AuthenticationHandler, AuthorizationHandler, Bootstrap,
    Interface, ExternalService,
    Component, Hook, Context, Provider, Page, Layout, Loading, ErrorBoundary,
    RouteHandler, ServerAction, ApiClient, FrontendService, Utility,
    ExpressApplication, Router, HttpRoute, ErrorMiddleware
}
