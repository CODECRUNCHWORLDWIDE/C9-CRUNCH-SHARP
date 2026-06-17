// Exercise 4 — The Browser Side of One Contract: a gRPC-Web Client That Calls
// the SAME Workshop Service the MAUI Client Calls.
//
// The Blazor admin (C#) consumes the contract via Grpc.Net.Client.Web and a
// GrpcWebHandler (Lecture 2 §6). But the point of this exercise is to PROVE the
// contract is genuinely language-neutral: a plain TypeScript browser client,
// generated from the SAME workshop.proto, reaches the SAME service over
// gRPC-Web. If a TS client and the .NET clients all compile against one proto
// and all hit one service, the contract really is the source of truth.
//
// Goal: configure a gRPC-Web client in TypeScript, attach the OIDC bearer
// token, call CreateLesson and ListPendingSubmissions, handle the gRPC status.
//
// Project layout:
//
//   admin-web/
//     package.json
//     proto/workshop.proto         <-- the SAME file, copied or symlinked
//     src/generated/               <-- protoc-gen-grpc-web output
//     src/workshopClient.ts        <-- this file
//
// Tooling (one-time):
//   npm install grpc-web google-protobuf
//   npm install -D protoc-gen-grpc-web ts-protoc-gen
//   # generate the TS stubs from the proto (grpc-web mode):
//   protoc -I=proto workshop.proto \
//     --js_out=import_style=commonjs:src/generated \
//     --grpc-web_out=import_style=typescript,mode=grpcwebtext:src/generated
//
// Server requirement (Lecture 2 §6): the backend must have
//   app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
//   app.MapGrpcService<WorkshopService>().EnableGrpcWeb().RequireCors("grpc-web");
// and the CORS policy MUST expose Grpc-Status and Grpc-Message, or every call
// fails with a "no status" error even though the server responded.
//
// Acceptance criteria:
//   1. `npm run build` (tsc) succeeds with no type errors.
//   2. createLesson() returns a Lesson whose id is a non-empty string.
//   3. A call without a token fails with grpc status UNAUTHENTICATED (16); the
//      client surfaces it as a typed error, not a generic network failure.
//   4. listPending() returns the submission created by the learner flow.
//   5. The Network tab shows application/grpc-web-text requests (NOT regular
//      JSON), proving the browser is speaking the gRPC-Web framing.

import { WorkshopClient } from "./generated/WorkshopServiceClientPb";
import {
  CreateLessonRequest,
  ListPendingSubmissionsRequest,
  Lesson,
  Submission,
} from "./generated/workshop_pb";
import { RpcError, StatusCode } from "grpc-web";

// The OIDC token provider. In the real admin this is wired to the same
// Keycloak realm the MAUI client and the backend use; here it is injected so
// the client stays testable.
export interface TokenProvider {
  getAccessToken(): Promise<string>;
}

export class WorkshopApi {
  private readonly client: WorkshopClient;

  constructor(
    baseUrl: string,
    private readonly tokens: TokenProvider,
  ) {
    // The gRPC-Web client. baseUrl points at the backend; the browser speaks
    // gRPC-Web framing, the server's UseGrpcWeb middleware un-frames it into a
    // normal gRPC call against WorkshopService.
    this.client = new WorkshopClient(baseUrl, null, null);
  }

  // Attach the bearer token as gRPC metadata. gRPC-Web carries metadata as
  // HTTP headers; "authorization" becomes the Authorization header the same
  // JWT middleware on the backend validates.
  private async authMetadata(): Promise<{ authorization: string }> {
    const token = await this.tokens.getAccessToken();
    return { authorization: `Bearer ${token}` };
  }

  async createLesson(title: string, body: string): Promise<Lesson> {
    const request = new CreateLessonRequest();
    request.setTitle(title);
    request.setBody(body);

    const metadata = await this.authMetadata();
    try {
      // The promise-based call style (grpc-web >= 1.x). Note: there is NO
      // instructor_id on the request — identity is the token's sub claim,
      // read server-side. The browser cannot assert who it is.
      return await this.client.createLesson(request, metadata);
    } catch (err) {
      throw this.translate(err);
    }
  }

  async listPending(pageSize = 50): Promise<Submission[]> {
    const request = new ListPendingSubmissionsRequest();
    request.setPageSize(pageSize);

    const metadata = await this.authMetadata();
    try {
      const response = await this.client.listPendingSubmissions(request, metadata);
      return response.getSubmissionsList();
    } catch (err) {
      throw this.translate(err);
    }
  }

  // Translate a gRPC status into a typed application error. The grpc-web
  // RpcError carries the same StatusCode the .NET client's RpcException
  // carries — one contract, two languages, one error model.
  private translate(err: unknown): Error {
    const rpc = err as RpcError;
    switch (rpc.code) {
      case StatusCode.UNAUTHENTICATED:
        return new WorkshopAuthError("Sign-in required or token expired.");
      case StatusCode.NOT_FOUND:
        return new WorkshopNotFoundError(rpc.message);
      case StatusCode.INVALID_ARGUMENT:
        return new WorkshopValidationError(rpc.message);
      default:
        return new Error(`gRPC call failed (${rpc.code}): ${rpc.message}`);
    }
  }
}

export class WorkshopAuthError extends Error {}
export class WorkshopNotFoundError extends Error {}
export class WorkshopValidationError extends Error {}

// ----------------------------------------------------------------------------
// TODO(you):
//   1. Implement a KeycloakTokenProvider that performs the OIDC code+PKCE flow
//      against the same realm the backend validates against, caches the token,
//      and refreshes it before expiry. Return it from getAccessToken().
//   2. Wire a moderation-queue component that calls listPending() on mount and
//      renders each Submission (id, learnerId, content, submittedAt).
//   3. Verify in the Network tab that requests are content-type
//      application/grpc-web-text and that the Grpc-Status response header is
//      present (if it is missing, the server CORS policy is not exposing it).
//   4. Confirm that calling createLesson() with no token throws a
//      WorkshopAuthError (grpc status 16 / UNAUTHENTICATED), not a generic
//      network error — that proves the backend auth is enforced for the
//      browser client exactly as for the MAUI client.
// ----------------------------------------------------------------------------
