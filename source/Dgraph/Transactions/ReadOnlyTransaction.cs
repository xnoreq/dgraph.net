/*
 * Copyright 2020 Dgraph Labs, Inc. and Contributors
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
 
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Api;
using FluentResults;
using Grpc.Core;

namespace Dgraph.Transactions
{

    internal class ReadOnlyTransaction : IQuery {

        public TransactionState TransactionState { get; protected set; }

        protected readonly IDgraphClientInternal Client;

        protected readonly TxnContext Context;

        protected readonly bool ReadOnly;

        protected readonly bool BestEffort;

        internal ReadOnlyTransaction(IDgraphClientInternal client, bool bestEffort) : this(client, true, bestEffort) { }

        protected ReadOnlyTransaction(IDgraphClientInternal client, bool readOnly, bool bestEffort) {
            Client = client;
            ReadOnly = readOnly;
            BestEffort = bestEffort;
            TransactionState = TransactionState.OK;
            Context = new TxnContext();
        }

        public async Task<FluentResults.Result<Response>> Query(
            string queryString, 
            CallOptions? options = null
        ) {
            return await QueryWithVars(queryString, null, options);
        }

        public async Task<FluentResults.Result<Response>> QueryWithVars(
            string queryString, 
            IDictionary<string, string> varMap = null,
            CallOptions? options = null
        ) {

            AssertNotDisposed();

            if (TransactionState != TransactionState.OK) {
                return Result.Fail<Response>(
                    new TransactionNotOK(TransactionState.ToString()));
            }

            try {
                var request = new Api.Request()
                {
                    Query = queryString,
                    StartTs = Context.StartTs,
                    Hash = Context.Hash,
                    ReadOnly = ReadOnly,
                    BestEffort = BestEffort
                };
                if (varMap != null)
                {
                    request.Vars.Add(varMap);
                }

                var response = await Client.DgraphExecute(
                    async (dg) => 
                        Result.Ok<Response>(
                            new Response(await dg.QueryAsync(
                                request, 
                                options ?? new CallOptions(null, null, default(CancellationToken)))
                        )),
                    (rpcEx) => 
                        Result.Fail<Response>(new FluentResults.ExceptionalError(rpcEx))
                );

                if (response.IsFailed) {
                    return response;
                }

                var err = MergeContext(response.Value.DgraphResponse.Txn);

                if (err.IsSuccess) {
                    return response;
                } else {
                    return err.ToResult<Response>();
                }

            } catch (Exception ex) {
                return Result.Fail<Response>(new FluentResults.ExceptionalError(ex));
            }
        }

        protected FluentResults.Result MergeContext(TxnContext srcContext) {
            if (srcContext == null) {
                return Result.Ok();
            }

            if (Context.StartTs == 0) {
                Context.StartTs = srcContext.StartTs;
            }

            if (Context.StartTs != srcContext.StartTs) {
                return Result.Fail(new StartTsMismatch());
            }

            Context.Hash = srcContext.Hash;

            Context.Keys.Add(srcContext.Keys);
            Context.Preds.Add(srcContext.Preds);

            return Result.Ok();
        }

        protected virtual void AssertNotDisposed() { }

    }

}