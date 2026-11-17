// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Vida.Propostas.Operacao.InspecaoDeEntidades.EspiarAssinaturaAsync(System.String,System.Boolean,System.Threading.CancellationToken)~System.Threading.Tasks.Task{Vida.Propostas.Contratos.MensagemEspiada[]}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Vida.Propostas.Operacao.InspecaoDeEntidades.EspiarFilaAsync(System.Boolean,System.Threading.CancellationToken)~System.Threading.Tasks.Task{Vida.Propostas.Contratos.MensagemEspiada[]}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Vida.Propostas.Operacao.InspecaoDeEntidades.ListarRegrasAsync(System.Threading.CancellationToken)~System.Threading.Tasks.Task{Vida.Propostas.Contratos.RegrasDaAssinatura[]}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Vida.Propostas.Operacao.ReprocessamentoDaDlq.LiberarAsync(Azure.Messaging.ServiceBus.ServiceBusReceiver,Azure.Messaging.ServiceBus.ServiceBusReceivedMessage)~System.Threading.Tasks.Task")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Vida.Propostas.Operacao.ReprocessamentoDaDlq.ReprocessarAsync(System.Threading.CancellationToken)~System.Threading.Tasks.Task{Vida.Propostas.Contratos.ResultadoDoReprocessamento}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Vida.Propostas.Controllers.PropostasController.CriarAsync(Vida.Propostas.Contratos.NovaProposta,System.String,System.Threading.CancellationToken)~System.Threading.Tasks.Task{Microsoft.AspNetCore.Mvc.ActionResult{Vida.Propostas.Contratos.PropostaResposta}}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Vida.Propostas.Controllers.PropostasController.Repetida(Vida.Propostas.Dados.PropostaRegistrada)~Microsoft.AspNetCore.Mvc.OkObjectResult")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Vida.Propostas.Outbox.DespachanteDoOutbox.MarcarPublicadasAsync(Vida.Propostas.Dados.BancoDePropostas,System.Collections.Generic.List{Vida.Propostas.Outbox.MensagemDeSaida},System.DateTimeOffset,System.Threading.CancellationToken)~System.Threading.Tasks.Task")]
