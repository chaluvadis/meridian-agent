global using System;
global using System.Diagnostics;
global using System.Globalization;
global using System.Net.Http.Json;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Text.RegularExpressions;

global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Infrastructure;
global using Microsoft.EntityFrameworkCore.Migrations;
global using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.Extensions.Options;

global using MeridianAgent.Application.Abstractions;
global using MeridianAgent.Application.DTOs;
global using MeridianAgent.Domain;
global using MeridianAgent.Domain.AgentContracts;
global using MeridianAgent.Domain.Entities;
global using MeridianAgent.Domain.Exceptions;
global using MeridianAgent.Infrastructure.AI;
global using MeridianAgent.Infrastructure.Data;
global using Polly;
