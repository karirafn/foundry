using System.Reflection;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Architecture test: every endpoint that declares an application/problem+json response
/// must expose only ProblemHttpResult arms in its Results&lt;...&gt; return-type union.
/// Endpoints that return bare IResult/Task&lt;IResult&gt; while declaring a problem+json
/// status are also flagged — opaque return types defeat the contract.
/// </summary>
public sealed class ProblemDetailsContractTests : IAsyncDisposable
{
    private static readonly Type ProblemHttpResultType = typeof(ProblemHttpResult);
    private static readonly Type IResultType = typeof(IResult);

    // Success arms are allowed alongside ProblemHttpResult in any union.
    // Conflict<T> is in here because CreateAccount/UpdateAccount use it with
    // application/json typed envelopes (ADR 0056), not application/problem+json —
    // so it never appears in the problem+json filtered set, but listing it here
    // makes the allow-list explicit.
    private static readonly HashSet<Type> SuccessArmOpenTypes =
    [
        typeof(Ok<>),
        typeof(Created<>),
        typeof(Accepted<>),
        typeof(Conflict<>),
        typeof(UnprocessableEntity<>),
    ];

    private static readonly HashSet<Type> SuccessArmClosedTypes =
    [
        typeof(Ok),
        typeof(Created),
        typeof(Accepted),
        typeof(NoContent),
        typeof(ContentHttpResult),
    ];

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public ProblemDetailsContractTests()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public void WhenEndpointDeclaresProblemJson_EveryErrorArmMustBeProblemHttpResult()
    {
        // Arrange
        EndpointDataSource dataSource =
            _factory.Services.GetRequiredService<EndpointDataSource>();

        // Act
        List<string> violations = CollectViolations(dataSource.Endpoints.OfType<RouteEndpoint>());

        // Assert
        violations.ShouldBeEmpty(
            "every endpoint declaring application/problem+json must return only " +
            "ProblemHttpResult for its error arms");
    }

    /// <summary>
    /// Red-probe test: directly exercises the detection logic with a fabricated endpoint
    /// that violates the contract (Results&lt;Ok, BadRequest&lt;string&gt;&gt; + ProducesProblem(400)).
    /// This confirms the check goes RED on a real regression and is not trivially green.
    /// </summary>
    [Fact]
    public void WhenEndpointHasNonCompliantErrorArm_CheckDetectsViolation()
    {
        // Arrange — build a synthetic RouteEndpoint with a deliberately bad return type.
        // The handler method returns Results<Ok, BadRequest<string>>, which violates the
        // contract because BadRequest<string> is not ProblemHttpResult.
        MethodInfo badHandlerMethod = typeof(ProblemDetailsContractTests)
            .GetMethod(nameof(BadHandlerThatReturnsBadRequestString),
                BindingFlags.Static | BindingFlags.NonPublic)
            .ShouldNotBeNull("BadHandlerThatReturnsBadRequestString must be a private static method");

        RouteEndpoint probe = BuildProbeEndpoint(
            "/api/test/probe-bad-endpoint",
            badHandlerMethod,
            problemStatusCode: StatusCodes.Status400BadRequest);

        // Act
        List<string> violations = CollectViolations([probe]);

        // Assert — the non-compliant arm must be detected
        violations.ShouldNotBeEmpty(
            "the deliberately non-compliant probe endpoint must produce at least one violation");
        violations.ShouldContain(
            v => v.Contains("BadRequest`1"),
            "the violation message must name the offending arm type 'BadRequest`1'");
    }

    // Deliberately non-compliant handler: returns BadRequest<string> but the endpoint
    // would declare ProducesProblem(400). Used only by WhenEndpointHasNonCompliantErrorArm_CheckDetectsViolation.
#pragma warning disable CA1859 // suppress "use concrete type" — the return type is the point of the test
    private static Results<Ok, BadRequest<string>> BadHandlerThatReturnsBadRequestString() =>
        TypedResults.Ok();
#pragma warning restore CA1859

    private static RouteEndpoint BuildProbeEndpoint(
        string pattern,
        MethodInfo handlerMethodInfo,
        int problemStatusCode)
    {
        EndpointMetadataCollection metadata = new(
            handlerMethodInfo,
            new ProducesProblemMetadata(problemStatusCode));

        return new RouteEndpoint(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: RoutePatternFactory.Parse(pattern),
            order: 0,
            metadata: metadata,
            displayName: "ProbeEndpoint");
    }

    private static List<string> CollectViolations(IEnumerable<RouteEndpoint> endpoints)
    {
        List<string> violations = [];

        foreach (RouteEndpoint endpoint in endpoints)
        {
            IReadOnlyList<IProducesResponseTypeMetadata> producesMetadata =
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();

            // Collect status codes that declare application/problem+json responses.
            // .ProducesProblem(N) emits this content type; .Produces<T>(N) emits application/json.
            List<int> problemStatusCodes = producesMetadata
                .Where(m => m.ContentTypes.Contains("application/problem+json"))
                .Select(m => m.StatusCode)
                .ToList();

            if (problemStatusCodes.Count == 0)
            {
                continue;
            }

            string routePattern = endpoint.RoutePattern.RawText ?? "(unknown route)";

            // Recover the handler MethodInfo from endpoint metadata.
            // Minimal APIs stamp the handler delegate's MethodInfo into endpoint metadata.
            MethodInfo? methodInfo = endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault();

            if (methodInfo is null)
            {
                // MethodInfo absent — this is itself a contract violation because we cannot
                // verify the arm types. Report it rather than silently skipping.
                violations.Add(
                    $"Route '{routePattern}' declares problem+json status codes " +
                    $"[{string.Join(", ", problemStatusCodes)}] but exposes no MethodInfo " +
                    "in endpoint metadata — unable to verify return-type arms.");
                continue;
            }

            // Unwrap Task<T> / ValueTask<T> to get the inner return type.
            Type returnType = UnwrapAsyncReturnType(methodInfo.ReturnType);

            // SECONDARY GUARD: a bare IResult return defeats the contract entirely.
            if (returnType == IResultType)
            {
                violations.Add(
                    $"Route '{routePattern}' declares problem+json status codes " +
                    $"[{string.Join(", ", problemStatusCodes)}] but returns bare IResult, " +
                    "which makes union-arm verification impossible. Narrow to Results<...>.");
                continue;
            }

            // If the return type is not a closed Results<...> generic, we can't enumerate arms.
            if (!IsClosedResultsGeneric(returnType))
            {
                // Non-Results<> return that is not IResult: allow it and move on.
                // (This covers edge cases such as a lambda returning a concrete ProblemHttpResult.)
                continue;
            }

            // Enumerate each type argument (arm) of the Results<T1, T2, ...> union.
            Type[] arms = returnType.GetGenericArguments();

            foreach (Type arm in arms)
            {
                if (IsSuccessArm(arm))
                {
                    continue;
                }

                if (arm == ProblemHttpResultType)
                {
                    continue;
                }

                // This arm is neither a recognised success type nor ProblemHttpResult.
                violations.Add(
                    $"Route '{routePattern}' declares problem+json status codes " +
                    $"[{string.Join(", ", problemStatusCodes)}] but has error arm " +
                    $"'{arm.Name}' in Results<{string.Join(", ", arms.Select(a => a.Name))}>. " +
                    "All error arms must be ProblemHttpResult.");
            }
        }

        return violations;
    }

    private static Type UnwrapAsyncReturnType(Type returnType)
    {
        if (returnType.IsGenericType)
        {
            Type genericDef = returnType.GetGenericTypeDefinition();
            if (genericDef == typeof(Task<>) || genericDef == typeof(ValueTask<>))
            {
                return returnType.GetGenericArguments()[0];
            }
        }

        return returnType;
    }

    private static bool IsClosedResultsGeneric(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        // Results<T1>, Results<T1, T2>, ..., Results<T1, ..., T6> — all share
        // the same open-generic name but different arity.
        Type openGeneric = type.GetGenericTypeDefinition();
        return openGeneric.FullName?.StartsWith("Microsoft.AspNetCore.Http.HttpResults.Results`",
            StringComparison.Ordinal) == true;
    }

    private static bool IsSuccessArm(Type arm)
    {
        if (SuccessArmClosedTypes.Contains(arm))
        {
            return true;
        }

        if (arm.IsGenericType && SuccessArmOpenTypes.Contains(arm.GetGenericTypeDefinition()))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Minimal IProducesResponseTypeMetadata implementation for the red-probe test —
    /// stamps a single application/problem+json status code onto a synthetic endpoint.
    /// </summary>
    private sealed class ProducesProblemMetadata(int statusCode) : IProducesResponseTypeMetadata
    {
        public Type? Type => null;
        public int StatusCode => statusCode;
        public IEnumerable<string> ContentTypes => ["application/problem+json"];
    }
}
