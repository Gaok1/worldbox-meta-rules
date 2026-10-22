# Meta Rules para WorldBox

![Meta Rules](metaRules.png)

Crie regras para **um meta individual**: aquele exército, clã, cultura, religião, cidade, reino, idioma, família ou subespécie. As regras não se aplicam automaticamente a todos os metas do mesmo tipo.

Feito para WorldBox 0.51.2 / build 719 com NeoModLoader. A interface usa janelas e abas do jogo, no estilo do Diplomacy Overhaul, com os devidos créditos pela biblioteca visual MIT.

## Como usar

1. Abra a janela normal de um idioma, reino, cultura ou outro meta específico.
2. Clique na aba **Regras**, com ícone de opções.
3. O editor abre diretamente naquele meta, com seu próprio brasão e nome. **Declarações** e **Exigências** aparecem juntas, sem abas ou lista lateral.

Cada seção tem um botão **+ Adicionar**. Regras usam os ícones nativos de traços, itens e atributos e o brasão real de cada meta. O seletor segue a mesma ordem e os mesmos grupos dos editores de traços e de equipamentos do jogo, com rolagem contínua. A janela tem o tamanho dos editores do jogo e usa a mesma barra de rolagem, os mesmos botões de traços e itens e os brasões nativos. Passe o mouse para ver os nomes; clique em uma regra para editá-la. A busca aparece apenas no seletor ao adicionar uma regra.

### Exigências

Defina quem pode entrar. Cada condição pode exigir um traço ou pertencimento a outro meta específico. Combine TODAS ou QUALQUER condição, com a opção NÃO. Todas as restrições ativas precisam ser atendidas.

Exemplo para a Guarda Imperial:

- Pertence ao clã Casa do Sol.
- Possui o traço Cavaleiro.

As exigências valem para novos membros e para os atuais. Quem não cumprir sai nas próximas atualizações, inclusive ao perder um traço ou vínculo exigido. No exército, isso também vale para o capitão. O exército configurado mantém suas regras se ficar vazio, enquanto seu reino existir.

### Declarações

Defina o que os membros recebem:

- Um traço enquanto pertencerem ao grupo.
- Um traço permanente, que continua após a saída.
- Um equipamento (arma, capacete, armadura, botas, anel ou amuleto), enquanto pertencer ou permanente.
- Entrada em outro grupo: "todo mundo deste reino fala Latim", "todo membro do clã serve neste exército e mora naquela cidade" (idioma, cultura, religião, clã, reino, cidade ou exército). Só acontece quando o jogo permite e sem quebrar nenhuma exigência; mudar de cidade também muda o reino e tira do exército antigo. Família e subespécie não podem ser concedidas.
- Um bônus de vida, dano, armadura, velocidade, velocidade de ataque, inteligência, habilidade militar, diplomacia ou administração enquanto pertencerem.

Exemplo: todos os falantes de um idioma específico recebem um traço; todos os soldados de um exército recebem +10 de armadura.

Traços anteriores ao benefício são preservados. Se vários grupos concedem o mesmo traço temporário, ele só é retirado quando não houver mais fontes ativas. Traços conflitantes são opostos ou traços que se removem ao serem adicionados (outro tier, escola ou desejo do mesmo tipo, via `traits_to_remove`). Quando várias fontes declaram traços conflitantes, o traço de um item vence o dos grupos do portador enquanto ele o usa; fora isso, vence o primeiro grupo (exército, clã, cultura, religião, cidade, reino, idioma, família, subespécie). A mesma prioridade vale para entrada em grupos e equipamentos declarados: um anel que concede um idioma vence o idioma da espécie enquanto é usado. O traço sobrescrito volta quando a fonte vencedora acaba. Traços conflitantes que não podem ser removidos nunca são apagados à força. Os bônus de atributos se somam entre grupos, sem acumular a cada atualização.

O equipamento é entregue **uma vez por pertencimento**, substituindo o item daquele espaço (o antigo vai para o depósito da cidade). Se depois a criatura trocar, melhorar ou quebrar o item, o mod não o recria. Ao sair, o item "enquanto pertencer" só é removido se ainda for o mesmo; o permanente fica. Só criaturas que usam itens recebem equipamento; se vários grupos declararem o mesmo espaço, vale o primeiro.

### Ver por que uma regra não vale

Cada declaração sabe a quem alcança. Na visão geral, uma declaração que não vale para todos os membros ganha um ponto colorido (dourado: sobrescrita ou inativa; vermelho: bloqueada) e uma linha em **Não está valendo** com a contagem e o motivo principal, por exemplo `0/11 · Reino Miners Of Bam Dum exige Idioma: Geric`. Passe o mouse para ver todos os motivos com contagem; clique para editar. O editor da declaração mostra um cartão **Situação** com uma barra dividida por motivo e prevê o que uma mudança ainda não salva faria. Em **Membros**/**Portadores**, um `!` marca as criaturas que alguma declaração não alcança e explica por quê. Adicionar membro/portador explica a recusa do mesmo jeito.

Os motivos citam o que recusa: uma exigência do destino ou de um grupo que a criatura mantém (com a condição), um limite do jogo (Raízes Verdadeiras, espécie sem fala ou memória, clã cheio, governantes, cidade que não aceita, moradia do exército), uma remoção, um item amaldiçoado ou outra fonte que vence (`Equipamento Anel concede Idioma Y no lugar`). Um traço trocado depois pelo jogo ou por outro mod (uma promoção natural sobre um tier concedido) aparece como inativo. Grupos grandes analisam os primeiros 400 membros.

### Criar um meta pelo seletor

Use o **+** na seção do tipo desejado para criar um idioma, cultura, religião ou clã vazio. No seletor de exigências, também é possível criar famílias e subespécies. O **+** de exército identifica a cidade; só aparece para cidades sem exército, respeitando o reino da origem quando houver. Cada cidade mantém o limite nativo de um exército. A criação continua disponível durante a busca, mesmo quando ainda não existe nenhum meta daquele tipo.

O meta nasce com **zero membros** e abre na própria janela do jogo. Edite nome, emblema, cores e traits disponíveis pelos controles nativos. **Voltar à regra** ou o botão de voltar do jogo retoma seu rascunho com o novo meta selecionado. A regra só entra em vigor ao salvar; cancelar a regra não apaga o meta criado.

A espécie segue o grupo de origem ou o portador do equipamento quando possível; grupos sociais usam humanos como alternativa quando precisam de modelos de nomes. Nenhum fundador é inventado e ninguém é transferido. Metas criados pelo editor são preservados vazios, inclusive após salvar e carregar. Exércitos continuam dependendo da cidade e do reino. Cidades e reinos precisam de território e devem ser fundados pelas ferramentas do jogo.

### Regras de um equipamento individual

Abra os detalhes de um item e clique em **Regras**. As declarações são aplicadas ao **portador**: traços temporários ou permanentes, bônus de atributos e entrada em outros metas. As exigências definem quem pode usar esse item, por traços, pertencimento a metas ou uso de outro equipamento específico. Isso vale para aquele item, identificado pelo ID, e não para todas as cópias do mesmo modelo.

Exemplo: uma espada só pode ser usada por membros da Casa do Sol; enquanto equipada, concede Corajoso, +10 de dano e entrada em uma religião específica. Também é possível exigir que um membro de um meta esteja usando um equipamento específico.

Itens guardados não concedem benefícios. Ao desequipar, trocar, destruir ou pausar, os benefícios temporários desaparecem nas próximas verificações; traços permanentes e vínculos concedidos continuam. Fontes de traços e bônus são combinadas entre grupos e equipamentos. Declarações de outros equipamentos usam espaços diferentes do item que as concede, para preservar a fonte dos benefícios. Mudanças de meta feitas pelo mod também respeitam as exigências dos equipamentos mantidos.

Quem não cumprir as exigências é desequipado na próxima verificação. O item volta ao depósito da cidade, se houver; sem cidade, o item configurado é preservado sem portador. Isso também vale para itens amaldiçoados. **Portadores → Desequipar** retira o item imediatamente, mesmo com as regras pausadas, e bloqueia a criatura enquanto as regras estiverem ativas; **Readmitir** libera o uso novamente.

**Adicionar portador** entrega este item exato, com ou sem regras, a qualquer criatura escolhida (com busca e filtro de elegíveis). Ele sai do portador atual ou do depósito da cidade; o item que a nova portadora usava nesse espaço vai para o depósito da cidade dela, ou se perde se ela não tiver cidade. Exigências, remoções, itens amaldiçoados e espécies que não podem usar o item são respeitados. Os grupos da nova portadora são checados contra os traços que ela terá depois que as concessões do item se acomodarem: a entrega é recusada, citando o grupo, se substituir ou sobrescrever um traço que um deles exige. O seletor de condições mostra nome, ID e portador/local do item, com busca e até 200 resultados por vez.

### Membros

O botão **+ Adicionar membros** aparece no início do editor. O filtro **Podem entrar** mostra apenas candidatos elegíveis; os botões indisponíveis explicam o bloqueio ao passar o mouse.

A página **Membros** lista as criaturas do meta (com busca; clique no nome para abrir). **Adicionar membros** busca criaturas vivas por nome ou ID e transfere a selecionada para este meta, respeitando exigências e limites do jogo. Família e subespécie só permitem transferência dentro da mesma espécie; mudar de família não altera pais/ascendência. **Remover** expulsa e mantém fora enquanto as regras estiverem ativas. **Readmitir** retira esse bloqueio; depois use **Adicionar** para trazer de volta imediatamente.

### Configurações do mod

Abra **Configurações do mod** no editor ou nas opções do Meta Rules no NeoModLoader. **Ignorar bloqueios de conversão cultural** é global, persistente e desativada por padrão. Ativada, declarações e adições manuais podem superar `true_roots` (True Roots), mas continuam respeitando suas exigências, remoções e limites estruturais do jogo. A conversão natural do jogo não muda. Desativar não desfaz transferências já realizadas; a alteração vale nas próximas verificações.

**Sobrescrever traços conflitantes** vem ativada por padrão: um traço declarado substitui os opostos que a criatura tem (como os tiers de outro mod), e uma concessão temporária os devolve quando termina. Desativada, concessões conflitantes são ignoradas.

O editor recusa combinações de traços impossíveis: traços opostos declarados, uma exigência que pede opostos (ou um traço e sua negação) ao mesmo tempo, ou um traço declarado que quebra a exigência da própria regra. Esses traços aparecem esmaecidos no seletor.

### Detalhes e opções

O cabeçalho mostra membros, incompatibilidades e o botão Ativo/Pausado do meta inteiro. Cada regra mostra seus controles (TODAS/ALGUMA, duração, ativada) ao ser aberta; a página **?** explica tudo. Benefícios temporários são retirados nas próximas atualizações; os permanentes continuam.

## Instalação

Extraia o pacote para `worldbox/Mods/MetaRules/`, deixando o `mod.json` diretamente nessa pasta, e reinicie o jogo. O NeoModLoader compila o código ao iniciar.

O idioma acompanha a configuração do jogo: Português (Brasil) — usado também para Português de Portugal — ou inglês; qualquer outro idioma usa o inglês. Regras ficam no próprio save, junto do meta correspondente.

## Limites da primeira versão

- A saída de uma cidade ou reino usa a saída nativa de facção: encerra a cidadania e o serviço militar e devolve o personagem ao seu reino neutro padrão.
- As conquistas primeiro mantêm os moradores no reino da cidade para preservar a coerência do jogo; a elegibilidade é verificada na próxima atualização.
- Uma criatura não pode ficar sem subespécie. Se não cumprir as exigências, só muda para outra subespécie compatível da mesma espécie. Sem alternativa, permanece e aparece como incompatível. A atribuição inicial continua permitida para criar novas criaturas corretamente.
- Declarações de traços e atributos se aplicam aos personagens, e não aos traços do próprio reino, cultura ou idioma.
- O processamento ocorre em lotes; em mundos grandes, mudanças podem levar alguns segundos.
- Compilação e testes do motor foram verificados; as novas telas e interações de equipamentos ainda precisam de conferência visual em partida.

Consulte o [README em inglês](README.md) para os comandos de desenvolvimento e os detalhes de persistência. Código MIT; os créditos da interface estão em [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES).
