global using System.Diagnostics;

global using Microsoft.AspNetCore.Components;
global using NavigationContext = Microsoft.AspNetCore.Components.Routing.NavigationContext;
global using Microsoft.AspNetCore.Components.Web;
global using Microsoft.AspNetCore.SignalR;
global using Microsoft.AspNetCore.SignalR.Client;
global using Microsoft.Extensions.Options;
global using Microsoft.JSInterop;

global using MeridianAgent.Application;
global using MeridianAgent.Application.Abstractions;
global using MeridianAgent.Application.DTOs;
global using MeridianAgent.Application.Orchestrator;
global using MeridianAgent.Application.State;
global using MeridianAgent.Domain.AgentContracts;
global using MeridianAgent.Domain.Entities;
global using MeridianAgent.Infrastructure.AI;
global using MeridianAgent.Infrastructure.AI.Agents;
global using MeridianAgent.Infrastructure.Data;
global using MeridianAgent.Infrastructure.External;
global using MeridianAgent.Infrastructure.Scheduling;
global using MeridianAgent.Infrastructure.Services;
global using MeridianAgent.Web.Components;
global using MeridianAgent.Web.Components.State;
global using MeridianAgent.Web.Hubs;
global using MudBlazor;
global using MudBlazor.Services;
global using Serilog;
